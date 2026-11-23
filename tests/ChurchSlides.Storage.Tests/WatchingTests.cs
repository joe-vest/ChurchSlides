using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Time;
using ChurchSlides.Storage.Watching;

namespace ChurchSlides.Storage.Tests;

public class WatchingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);

    private static ClassifiedFile Classify(TempFolder temp, params string[] parts) =>
        LibraryFileNames.Classify(new LibraryLayout(temp.Path), temp.Combine(parts));

    private static string G(int n) => Samples.I<Show>(n).ToString();

    // ---- LIB-007 debounce ----

    [Fact]
    public void LIB_007_twenty_events_in_300ms_give_one_due_path_500ms_after_the_last()
    {
        var d = new ChangeDebouncer();
        for (var i = 0; i < 20; i++) d.Add("a.json", T0.AddMilliseconds(i * 15));
        var last = T0.AddMilliseconds(19 * 15);

        Assert.Empty(d.TakeDue(last.AddMilliseconds(499)));
        Assert.Equal(["a.json"], d.TakeDue(last.AddMilliseconds(500)));
        Assert.Empty(d.TakeDue(last.AddSeconds(5)));
        Assert.Null(d.NextDueAt);
    }

    [Fact]
    public void LIB_007_two_paths_flush_separately_and_an_event_restarts_the_quiet_period()
    {
        var d = new ChangeDebouncer();
        d.Add("a.json", T0);
        d.Add("b.json", T0.AddMilliseconds(300));
        Assert.Equal(T0.AddMilliseconds(500), d.NextDueAt);

        Assert.Equal(["a.json"], d.TakeDue(T0.AddMilliseconds(500)));
        d.Add("B.JSON", T0.AddMilliseconds(700)); // same path, other case: restarts it
        Assert.Empty(d.TakeDue(T0.AddMilliseconds(800)));
        Assert.Equal(["b.json"], d.TakeDue(T0.AddMilliseconds(1200)));
    }

    // ---- LIB-009 names ----

    [Fact]
    public void LIB_009_object_files_named_by_id_are_classified_per_folder()
    {
        using var temp = new TempFolder();
        foreach (var (folder, kind) in new[]
        {
            ("shows", LibraryFileKind.Show), ("services", LibraryFileKind.Service), ("templates", LibraryFileKind.Template),
            ("service-templates", LibraryFileKind.ServiceTemplate), ("service-state", LibraryFileKind.ServiceState),
        })
        {
            var c = Classify(temp, folder, $"{G(1)}.json");
            Assert.Equal(LibraryFileClass.ObjectFile, c.Class);
            Assert.Equal(kind, c.Kind);
            Assert.Equal(Samples.I<Show>(1).Value, c.Id);
        }
        Assert.Equal(LibraryFileClass.ObjectFile, Classify(temp, "shows", $"{G(1)}.JSON").Class);
        Assert.Equal(LibraryFileKind.Categories, Classify(temp, "categories.json").Kind);
        Assert.Equal(LibraryFileKind.Settings, Classify(temp, "library.json").Kind);
    }

    [Theory]
    [InlineData("{0} (1).json", ProblemKind.ConflictCopy)]
    [InlineData("{0} (Joe's conflicted copy 2026-11-23).json", ProblemKind.ConflictCopy)]
    [InlineData("{0}-LAPTOP.json", ProblemKind.UnexpectedFile)]
    [InlineData("Amazing Grace.json", ProblemKind.UnexpectedFile)]
    public void LIB_009_other_json_names_in_an_object_folder_are_problems(string pattern, ProblemKind expected)
    {
        using var temp = new TempFolder();
        var c = Classify(temp, "shows", string.Format(pattern, G(1)));
        Assert.Equal(LibraryFileClass.Misnamed, c.Class);
        Assert.Equal(expected, c.Problem);
    }

    [Theory]
    [InlineData("shows", "{0}.json.saving")]
    [InlineData("shows", "{0}.json.tmp")]
    [InlineData("shows", "~${0}.json")]
    [InlineData("shows", "desktop.ini")]
    [InlineData("shows", "sub/{0}.json")]
    [InlineData("media", "image.json")]
    [InlineData("logs", "{0}.json")]
    [InlineData("", "notes.json")]
    public void LIB_009_noise_and_unknown_places_are_ignored(string folder, string pattern)
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, folder, string.Format(pattern, G(1)).Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(LibraryFileClass.Ignored, LibraryFileNames.Classify(new LibraryLayout(temp.Path), path).Class);
    }

    // ---- LIB-009 scan ----

    [Fact]
    public void LIB_009_scan_loads_only_good_files_and_lists_each_problem_once()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        Assert.True(lib.Shows.Save(Samples.Show(1), null).Succeeded);
        Assert.True(lib.Shows.Save(Samples.Show(2, "Second"), null).Succeeded);
        Assert.True(lib.Services.Save(Samples.Service(), null).Succeeded); // shares Show Copy Ids with nothing else
        var good = temp.Combine("shows", $"{G(1)}.json");
        File.Copy(good, temp.Combine("shows", $"{G(1)} (1).json"));
        File.Copy(good, temp.Combine("shows", $"{G(5)}.json")); // a Show holding another Show's Id
        File.WriteAllText(temp.Combine("shows", $"{G(6)}.json"), "{ not json");
        File.WriteAllText(temp.Combine("shows", $"{G(2)}.json.saving"), "half written");

        var scan = new FolderLibrary(temp.Path).Monitor.Scan();

        Assert.All(scan.Changes, c => Assert.Equal(ChangeType.Added, c.Type));
        Assert.Equal(
            [(LibraryFileKind.Show, (Guid?)Samples.I<Show>(1).Value), (LibraryFileKind.Show, Samples.I<Show>(2).Value), (LibraryFileKind.Service, Samples.I<Service>(7).Value)],
            scan.Changes.Select(c => (c.Kind, c.Id)).OrderBy(x => x.Kind).ThenBy(x => x.Item2));
        Assert.Equal(
            [
                (temp.Combine("shows", $"{G(1)} (1).json"), ProblemKind.ConflictCopy),
                (temp.Combine("shows", $"{G(5)}.json"), ProblemKind.CopiedFile),
                (temp.Combine("shows", $"{G(6)}.json"), ProblemKind.Unreadable),
            ],
            scan.Problems.Select(p => (p.Path, p.Kind)));
    }

    // ---- LIB-007 apply ----

    private static (FolderLibrary Mine, FolderLibrary Other, LibraryMonitor Monitor) Open(TempFolder temp)
    {
        var mine = new FolderLibrary(temp.Path);
        var other = new FolderLibrary(temp.Path); // "the other machine"
        mine.Monitor.Scan();
        return (mine, other, mine.Monitor);
    }

    [Fact]
    public void LIB_007_a_change_made_elsewhere_is_reported_with_the_new_stamp()
    {
        using var temp = new TempFolder();
        Assert.True(new FolderLibrary(temp.Path).Shows.Save(Samples.Show(1), null).Succeeded);
        var (_, other, monitor) = Open(temp);
        var path = temp.Combine("shows", $"{G(1)}.json");

        var saved = other.Shows.Save(Samples.Show(1, "Renamed"), other.Shows.Load(Samples.I<Show>(1))!.Stamp);
        var update = monitor.Apply([path]);

        var change = Assert.Single(update.Changes);
        Assert.Equal((LibraryFileKind.Show, ChangeType.Changed), (change.Kind, change.Type));
        Assert.True(saved.Stamp!.SameContentAs(change.Stamp));
        Assert.Empty(monitor.Apply([path]).Changes); // already seen
    }

    [Fact]
    public void LIB_007_rewriting_the_same_bytes_gives_nothing()
    {
        using var temp = new TempFolder();
        Assert.True(new FolderLibrary(temp.Path).Shows.Save(Samples.Show(1), null).Succeeded);
        var (_, _, monitor) = Open(temp);
        var path = temp.Combine("shows", $"{G(1)}.json");

        File.WriteAllBytes(path, File.ReadAllBytes(path));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));

        Assert.True(monitor.Apply([path]).IsEmpty);
    }

    [Fact]
    public void LIB_007_our_own_saves_and_deletes_give_nothing()
    {
        using var temp = new TempFolder();
        var (mine, _, monitor) = Open(temp);
        var path = temp.Combine("shows", $"{G(1)}.json");

        var saved = mine.Shows.Save(Samples.Show(1), null);
        Assert.True(monitor.Apply([path]).IsEmpty);

        var resaved = mine.Shows.Save(Samples.Show(1, "Retitled"), saved.Stamp);
        Assert.True(monitor.Apply([path]).IsEmpty);

        Assert.True(mine.Shows.Delete(Samples.I<Show>(1), resaved.Stamp).Succeeded);
        Assert.True(monitor.Apply([path]).IsEmpty);
    }

    [Fact]
    public void LIB_007_new_and_deleted_files_are_added_and_removed()
    {
        using var temp = new TempFolder();
        var (_, other, monitor) = Open(temp);
        var path = temp.Combine("shows", $"{G(1)}.json");

        var saved = other.Shows.Save(Samples.Show(1), null);
        Assert.Equal(ChangeType.Added, Assert.Single(monitor.Apply([path]).Changes).Type);

        Assert.True(other.Shows.Delete(Samples.I<Show>(1), saved.Stamp).Succeeded);
        var removed = Assert.Single(monitor.Apply([path]).Changes);
        Assert.Equal((ChangeType.Removed, Samples.I<Show>(1).Value), (removed.Type, removed.Id!.Value));
    }

    [Fact]
    public void LIB_009_a_conflict_copy_appearing_and_disappearing_updates_problems_and_never_adds()
    {
        using var temp = new TempFolder();
        Assert.True(new FolderLibrary(temp.Path).Shows.Save(Samples.Show(1), null).Succeeded);
        var (_, _, monitor) = Open(temp);
        var copy = temp.Combine("shows", $"{G(1)} (1).json");

        File.Copy(temp.Combine("shows", $"{G(1)}.json"), copy);
        var appeared = monitor.Apply([copy]);
        Assert.Empty(appeared.Changes);
        Assert.True(appeared.ProblemsChanged);
        Assert.Equal(copy, Assert.Single(monitor.Problems).Path);

        File.Delete(copy);
        var gone = monitor.Apply([copy]);
        Assert.Empty(gone.Changes);
        Assert.True(gone.ProblemsChanged);
        Assert.Empty(monitor.Problems);
    }

    [Fact]
    public void LIB_007_saving_files_coming_and_going_give_nothing()
    {
        using var temp = new TempFolder();
        var (_, _, monitor) = Open(temp);
        var saving = temp.Combine("shows", $"{G(1)}.json.saving");
        Directory.CreateDirectory(temp.Combine("shows"));

        File.WriteAllText(saving, "partial");
        Assert.True(monitor.Apply([saving]).IsEmpty);
        File.Delete(saving);
        Assert.True(monitor.Apply([saving]).IsEmpty);
    }

    // ---- LIB-013 ----

    [Fact]
    public void LIB_013_state_categories_and_settings_changes_are_reported()
    {
        using var temp = new TempFolder();
        var (_, other, monitor) = Open(temp);
        var serviceId = Samples.I<Service>(7);

        Assert.True(other.ServiceStates.Save(ServiceState.Defaults(serviceId) with { Mode = ServiceMode.Live }).Succeeded);
        Assert.True(other.Categories.Save(Samples.Categories(), null).Succeeded);
        Assert.True(other.Settings.Save(new LibrarySettings { CcliLicenseNumber = "1" }, null).Succeeded);

        var update = monitor.Apply([temp.Combine("service-state", $"{serviceId}.json"), temp.Combine("categories.json"), temp.Combine("library.json")]);

        Assert.Equal(
            [(LibraryFileKind.ServiceState, (Guid?)serviceId.Value), (LibraryFileKind.Categories, null), (LibraryFileKind.Settings, null)],
            update.Changes.Select(c => (c.Kind, c.Id)));
    }

    // ---- AS-08 (storage level) ----

    [Fact]
    public void AS_08_a_show_changed_elsewhere_is_reported_then_save_conflicts_until_reloaded()
    {
        using var temp = new TempFolder();
        Assert.True(new FolderLibrary(temp.Path).Shows.Save(Samples.Show(1), null).Succeeded);
        var (mine, other, monitor) = Open(temp);
        var id = Samples.I<Show>(1);
        var loaded = mine.Shows.Load(id)!;

        Assert.True(other.Shows.Save(Samples.Show(1, "From the other machine"), other.Shows.Load(id)!.Stamp).Succeeded);
        var update = monitor.Apply([temp.Combine("shows", $"{G(1)}.json")]);
        Assert.Equal(ChangeType.Changed, Assert.Single(update.Changes).Type);

        var before = File.ReadAllBytes(temp.Combine("shows", $"{G(1)}.json"));
        var conflict = mine.Shows.Save(Samples.Show(1, "My edit"), loaded.Stamp);
        Assert.Equal(SaveOutcome.Conflict, conflict.Outcome);
        Assert.Equal(before, File.ReadAllBytes(temp.Combine("shows", $"{G(1)}.json")));

        var reloaded = mine.Shows.Load(id)!;
        Assert.Equal("From the other machine", reloaded.Result.Value!.Title);
        Assert.True(mine.Shows.Save(reloaded.Result.Value with { Title = "Merged" }, reloaded.Stamp).Succeeded);
    }

    // ---- LIB-010 ----

    [Fact]
    public void LIB_010_check_tells_what_is_wrong_with_the_chosen_folder()
    {
        using var temp = new TempFolder();
        Assert.Equal(LibraryFolderStatus.NotSet, LibraryFolder.Check(null));
        Assert.Equal(LibraryFolderStatus.NotSet, LibraryFolder.Check("  "));
        Assert.Equal(LibraryFolderStatus.Missing, LibraryFolder.Check(temp.Path));

        Directory.CreateDirectory(temp.Path);
        Assert.Equal(LibraryFolderStatus.Ok, LibraryFolder.Check(temp.Path)); // empty: a new Library

        var file = temp.Combine("a-file.txt");
        File.WriteAllText(file, "x");
        Assert.Equal(LibraryFolderStatus.NotAFolder, LibraryFolder.Check(file));

        Assert.True(new FolderLibrary(temp.Path).Shows.Save(Samples.Show(1), null).Succeeded);
        Assert.Equal(LibraryFolderStatus.Ok, LibraryFolder.Check(temp.Path));
    }

    [Fact]
    public void LIB_010_the_monitor_reports_unavailable_and_a_rescan_after_it_returns_gives_the_differences()
    {
        using var temp = new TempFolder();
        var first = new FolderLibrary(temp.Path);
        Assert.True(first.Shows.Save(Samples.Show(1), null).Succeeded);
        Assert.True(first.Shows.Save(Samples.Show(2, "Second"), null).Succeeded);
        var monitor = first.Monitor;
        monitor.Scan();

        var backup = temp.Path + "-away";
        Directory.Move(temp.Path, backup);
        Assert.True(monitor.Apply([temp.Combine("shows", $"{G(1)}.json")]).Unavailable);
        Assert.True(monitor.Rescan().Unavailable);

        Directory.CreateDirectory(temp.Path);
        Directory.Move(Path.Combine(backup, "shows"), temp.Combine("shows"));
        File.Delete(temp.Combine("shows", $"{G(2)}.json"));
        var other = new FolderLibrary(temp.Path);
        Assert.True(other.Shows.Save(Samples.Show(3, "Third"), null).Succeeded);

        var update = monitor.Rescan();
        Assert.False(update.Unavailable);
        Assert.Equal(
            [(ChangeType.Added, Samples.I<Show>(3).Value), (ChangeType.Removed, Samples.I<Show>(2).Value)],
            update.Changes.Select(c => (c.Type, c.Id!.Value)).OrderBy(x => x.Type).ThenBy(x => x.Value));

        Directory.Delete(backup, recursive: true);
    }

    // ---- FolderWatcher (real folder) ----

    private sealed class UpdateCollector
    {
        private readonly List<LibraryUpdate> _updates = [];
        public void Add(LibraryUpdate u) { lock (_updates) _updates.Add(u); }
        public List<LibraryUpdate> Snapshot() { lock (_updates) return [.. _updates]; }
    }

    private static bool WaitFor(Func<bool> condition, int milliseconds = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            Thread.Sleep(25);
        }
        return condition();
    }

    [Fact]
    public void LIB_007_the_real_watcher_reports_a_file_written_elsewhere_once()
    {
        using var temp = new TempFolder();
        var mine = new FolderLibrary(temp.Path);
        Directory.CreateDirectory(temp.Path);
        mine.Monitor.Scan();
        var updates = new UpdateCollector();
        using var watcher = new FolderWatcher(mine.Monitor, new SystemClock(), TimeSpan.FromMilliseconds(150));
        watcher.Updated += updates.Add;
        watcher.Start();

        Assert.True(new FolderLibrary(temp.Path).Shows.Save(Samples.Show(1), null).Succeeded);

        Assert.True(WaitFor(() => updates.Snapshot().Count > 0));
        Thread.Sleep(600); // a late second report would be a bug
        var change = Assert.Single(updates.Snapshot().SelectMany(u => u.Changes));
        Assert.Equal((LibraryFileKind.Show, ChangeType.Added), (change.Kind, change.Type));
    }

    [Fact]
    public void LIB_010_the_real_watcher_raises_unavailable_when_the_folder_is_deleted()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        Assert.True(lib.Shows.Save(Samples.Show(1), null).Succeeded);
        lib.Monitor.Scan();
        var raised = 0;
        using var watcher = new FolderWatcher(lib.Monitor, new SystemClock(), TimeSpan.FromMilliseconds(150));
        watcher.Unavailable += () => Interlocked.Increment(ref raised);
        watcher.Start();

        Directory.Delete(temp.Path, recursive: true);

        Assert.True(WaitFor(() => Volatile.Read(ref raised) > 0));
    }
}
