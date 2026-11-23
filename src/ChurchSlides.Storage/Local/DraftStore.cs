using System.Text.Json;

namespace ChurchSlides.Storage.Local;

public enum DraftKind { Show, Service, Template, ServiceTemplate }

/// <summary>
/// An autosaved, unsaved edit (SAFE-001/002). <see cref="Text"/> is the exact <c>VersionedFile</c> text of the object.
/// <see cref="BaseStamp"/> is the Library file's stamp when editing began (null for a new object), so a restore can tell
/// the Library file changed since. <see cref="SavedUtc"/> is the draft file's write time.
/// </summary>
public sealed record Draft(DraftKind Kind, Guid Id, string Text, FileStamp? BaseStamp, DateTime SavedUtc);

/// <summary>Damaged draft files are listed in <see cref="Problems"/> instead of being thrown or silently dropped.</summary>
public sealed record DraftListing(IReadOnlyList<Draft> Drafts, IReadOnlyList<string> Problems);

public interface IDraftStore
{
    SaveResult Save(DraftKind kind, Guid id, string text, FileStamp? baseStamp);

    /// <summary>Null when there is no readable draft.</summary>
    Draft? Load(DraftKind kind, Guid id);

    DraftListing List();

    /// <summary>Removes the draft. A draft that doesn't exist is fine.</summary>
    SaveResult Discard(DraftKind kind, Guid id);
}

/// <summary>One file per draft under <c>drafts\{kind}\{id}.json</c>, in machine-local storage and never in the Library.</summary>
public sealed class FileDraftStore(LocalDataPaths paths, AtomicWriteOptions? writeOptions = null) : IDraftStore
{
    private readonly AtomicFileWriter _writer = new(writeOptions);

    private string FolderFor(DraftKind kind) => Path.Combine(paths.DraftsFolder, kind.ToString().ToLowerInvariant());

    private string PathFor(DraftKind kind, Guid id) => Path.Combine(FolderFor(kind), id + ".json");

    public SaveResult Save(DraftKind kind, Guid id, string text, FileStamp? baseStamp)
    {
        var file = new DraftFile { BaseStamp = baseStamp, Content = text };
        return LocalFiles.Write(_writer, PathFor(kind, id), JsonSerializer.SerializeToUtf8Bytes(file, LocalJsonContext.Default.DraftFile));
    }

    public Draft? Load(DraftKind kind, Guid id) => TryLoad(kind, PathFor(kind, id), id, out _);

    public DraftListing List()
    {
        var drafts = new List<Draft>();
        var problems = new List<string>();

        foreach (var kind in Enum.GetValues<DraftKind>())
        {
            var folder = FolderFor(kind);
            if (!Directory.Exists(folder)) continue;

            foreach (var path in Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.OrdinalIgnoreCase))
            {
                if (!Guid.TryParse(Path.GetFileNameWithoutExtension(path), out var id))
                {
                    problems.Add($"{kind.ToString().ToLowerInvariant()}/{Path.GetFileName(path)}: the file name is not an Id.");
                    continue;
                }
                var draft = TryLoad(kind, path, id, out var problem);
                if (draft is not null) drafts.Add(draft);
                else problems.Add(problem!);
            }
        }
        return new(drafts, problems);
    }

    public SaveResult Discard(DraftKind kind, Guid id)
    {
        try
        {
            _writer.Delete(PathFor(kind, id));
            return new(SaveOutcome.Deleted);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(SaveOutcome.IoFailed, Error: ex.Message);
        }
    }

    private static Draft? TryLoad(DraftKind kind, string path, Guid id, out string? problem)
    {
        problem = null;
        var bytes = LocalFiles.TryRead(path);
        if (bytes is null) return null;

        try
        {
            var file = JsonSerializer.Deserialize(bytes, LocalJsonContext.Default.DraftFile);
            if (file?.Content is not null)
                return new(kind, id, file.Content, file.BaseStamp, File.GetLastWriteTimeUtc(path));
            problem = $"{kind.ToString().ToLowerInvariant()}/{Path.GetFileName(path)}: the draft has no content.";
        }
        catch (JsonException ex)
        {
            problem = $"{kind.ToString().ToLowerInvariant()}/{Path.GetFileName(path)}: {ex.Message}";
        }
        return null;
    }
}

internal sealed record DraftFile
{
    public int SchemaVersion { get; init; } = 1;
    public FileStamp? BaseStamp { get; init; }
    public string? Content { get; init; }
}
