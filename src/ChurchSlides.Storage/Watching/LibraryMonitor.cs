using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Serialization;

namespace ChurchSlides.Storage.Watching;

/// <summary>
/// Looks at Library files that may have changed and reports what is different from what it last saw (LIB-007, LIB-009, ARCH 8.3).
/// It remembers a content hash per loadable file, so our own saves and a sync tool re-stamping a file produce nothing.
/// Runs on whichever thread calls it (decision 8). Calls are serialized.
/// </summary>
public sealed class LibraryMonitor
{
    private readonly LibraryFileStore _files;
    private readonly object _gate = new();
    private readonly Dictionary<string, Known> _known = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LibraryProblem> _problems = new(StringComparer.OrdinalIgnoreCase);

    private sealed record Known(LibraryFileKind Kind, Guid? Id, string Sha256);

    private abstract record Inspection;
    private sealed record Missing : Inspection;
    private sealed record Good(FileStamp Stamp, Guid? Id) : Inspection;
    private sealed record Bad(ProblemKind Kind, string Message) : Inspection;

    internal LibraryMonitor(LibraryLayout layout, LibraryFileStore files)
    {
        Layout = layout;
        _files = files;
    }

    public LibraryLayout Layout { get; }

    public IReadOnlyList<LibraryProblem> Problems
    {
        get
        {
            lock (_gate) return SortedProblems();
        }
    }

    /// <summary>A full pass from nothing: every loadable file is reported as Added. Use at startup.</summary>
    public LibraryUpdate Scan()
    {
        lock (_gate)
        {
            _known.Clear();
            _problems.Clear();
            return ApplyLocked(EnumerateFiles());
        }
    }

    /// <summary>A full pass against what is remembered: reports only the differences. Use after the folder was unavailable or the watcher overflowed.</summary>
    public LibraryUpdate Rescan()
    {
        lock (_gate)
            return ApplyLocked(EnumerateFiles().Concat(_known.Keys).Concat(_problems.Keys).ToList());
    }

    /// <summary>Looks at each path as it is now. Event types don't matter (decision 3).</summary>
    public LibraryUpdate Apply(IEnumerable<string> paths)
    {
        lock (_gate) return ApplyLocked(paths);
    }

    /// <summary>Called by the store after every successful save or delete of ours. A null stamp means deleted.</summary>
    internal void RecordWrite(string path, FileStamp? stamp)
    {
        var fullPath = Path.GetFullPath(path);
        lock (_gate)
        {
            if (stamp is null)
            {
                _known.Remove(fullPath);
                return;
            }
            var classified = LibraryFileNames.Classify(Layout, fullPath);
            if (classified.Class is LibraryFileClass.ObjectFile or LibraryFileClass.Singleton)
            {
                _problems.Remove(fullPath);
                _known[fullPath] = new(classified.Kind, classified.Class == LibraryFileClass.ObjectFile ? classified.Id : null, stamp.Sha256);
            }
        }
    }

    private LibraryUpdate ApplyLocked(IEnumerable<string> paths)
    {
        if (!Directory.Exists(Layout.Root))
            return new([], SortedProblems(), ProblemsChanged: false, Unavailable: true);

        var before = SortedProblems();
        var changes = new List<LibraryChange>();
        foreach (var path in paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
            Evaluate(path, changes);

        var after = SortedProblems();
        return new(changes, after, !before.SequenceEqual(after));
    }

    private List<LibraryProblem> SortedProblems() =>
        [.. _problems.Values.OrderBy(p => p.Path, StringComparer.OrdinalIgnoreCase)];

    private void Evaluate(string path, List<LibraryChange> changes)
    {
        var classified = LibraryFileNames.Classify(Layout, path);
        if (classified.Class == LibraryFileClass.Ignored) return;

        switch (Inspect(path, classified))
        {
            case Missing:
                _problems.Remove(path);
                if (_known.Remove(path, out var gone))
                    changes.Add(new(gone.Kind, gone.Id, ChangeType.Removed, path, null));
                break;

            case Bad bad:
                _problems[path] = new(path, bad.Kind, bad.Message);
                if (_known.Remove(path, out var lost))
                    changes.Add(new(lost.Kind, lost.Id, ChangeType.Removed, path, null));
                break;

            case Good good:
                _problems.Remove(path);
                var existed = _known.TryGetValue(path, out var previous);
                if (existed && previous!.Sha256 == good.Stamp.Sha256) break;
                _known[path] = new(classified.Kind, good.Id, good.Stamp.Sha256);
                changes.Add(new(classified.Kind, good.Id, existed ? ChangeType.Changed : ChangeType.Added, path, good.Stamp));
                break;
        }
    }

    private Inspection Inspect(string path, ClassifiedFile classified)
    {
        if (!File.Exists(path)) return new Missing();

        var name = Path.GetFileName(path);
        if (classified.Class == LibraryFileClass.Misnamed)
            return classified.Problem == ProblemKind.ConflictCopy
                ? new Bad(ProblemKind.ConflictCopy, $"{name} looks like a sync conflict copy. It is not loaded.")
                : new Bad(ProblemKind.UnexpectedFile, $"{name} is not named after an Id. It is not loaded.");

        return classified.Kind switch
        {
            LibraryFileKind.Show => From(_files.Load<Show>(path, v => v.Id.Value), classified),
            LibraryFileKind.Service => From(_files.Load<Service>(path, v => v.Id.Value), classified),
            LibraryFileKind.Template => From(_files.Load<Template>(path, v => v.Id.Value), classified),
            LibraryFileKind.ServiceTemplate => From(_files.Load<ServiceTemplate>(path, v => v.Id.Value), classified),
            LibraryFileKind.ServiceState => From(_files.Load<ServiceState>(path, v => v.ServiceId.Value), classified),
            LibraryFileKind.Categories => From(_files.Load<CategoryList>(path), classified),
            _ => From(_files.Load<LibrarySettings>(path), classified),
        };
    }

    private static Inspection From<T>(LoadedFile<T>? loaded, ClassifiedFile classified) where T : class
    {
        if (loaded is null) return new Missing();
        var name = Path.GetFileName(loaded.Path);
        if (loaded.Result.Status == LoadStatus.Unreadable || loaded.Stamp is null)
            return new Bad(ProblemKind.Unreadable, loaded.Result.Error ?? $"{name} could not be read.");
        if (!loaded.NameMatchesId)
            return new Bad(ProblemKind.CopiedFile, $"{name} holds a different Id than its name. It looks like a copied or renamed file and is not loaded.");
        return new Good(loaded.Stamp, classified.Class == LibraryFileClass.ObjectFile ? classified.Id : null);
    }

    private List<string> EnumerateFiles()
    {
        var all = new List<string>();
        foreach (var folder in new[] { Layout.ShowsFolder, Layout.ServicesFolder, Layout.TemplatesFolder, Layout.ServiceTemplatesFolder, Layout.ServiceStateFolder })
            if (Directory.Exists(folder)) all.AddRange(Directory.EnumerateFiles(folder));
        all.Add(Layout.CategoriesFile);
        all.Add(Layout.SettingsFile);
        return all;
    }
}
