using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Serialization;

namespace ChurchSlides.Storage.Tests;

/// <summary>LIB-005 (atomic saves) and LIB-006 (overwrite protection).</summary>
public class SafeWriteTests
{
    private static AtomicWriteOptions Fast(Action<string>? beforeCommit = null, int attempts = 3) =>
        new() { Attempts = attempts, RetryDelay = TimeSpan.Zero, BeforeCommit = beforeCommit };

    private static string ShowPath(TempFolder temp, Show show) => temp.Combine("shows", $"{show.Id}.json");

    // ---- LIB-005 ----

    [Fact]
    public void LIB_005_a_leftover_saving_file_is_ignored_when_loading()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        var show = Samples.Show();
        lib.Shows.Save(show, null);
        File.WriteAllText(ShowPath(temp, show) + ".saving", "{ half a fil"); // a crash mid-write
        File.WriteAllText(temp.Combine("shows", $"{Guid.NewGuid()}.json.saving"), "{ also half");

        var all = lib.Shows.LoadAll();

        var only = Assert.Single(all);
        Assert.Equal(LoadStatus.Ok, only.Result.Status);
        Assert.Equal(show.Title, only.Result.Value!.Title);
    }

    [Fact]
    public void LIB_005_a_stale_saving_file_does_not_block_the_next_save()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        var show = Samples.Show();
        lib.Shows.Save(show, null);
        File.WriteAllText(ShowPath(temp, show) + ".saving", "garbage from a crash");
        var stamp = lib.Shows.Load(show.Id)!.Stamp;

        var result = lib.Shows.Save(show with { Title = "Renamed" }, stamp);

        Assert.Equal(SaveOutcome.Saved, result.Outcome);
        Assert.Equal("Renamed", lib.Shows.Load(show.Id)!.Result.Value!.Title);
        Assert.DoesNotContain(temp.Files(), f => f.EndsWith(".saving"));
    }

    [Fact]
    public void LIB_005_a_failure_between_write_and_replace_leaves_the_original_untouched()
    {
        using var temp = new TempFolder();
        var show = Samples.Show();
        new FolderLibrary(temp.Path).Shows.Save(show, null);
        var before = File.ReadAllBytes(ShowPath(temp, show));

        var crashing = new FolderLibrary(temp.Path, Fast(_ => throw new IOException("simulated crash")));
        var stamp = crashing.Shows.Load(show.Id)!.Stamp;
        var result = crashing.Shows.Save(show with { Title = "Never lands" }, stamp);

        Assert.Equal(SaveOutcome.IoFailed, result.Outcome);
        Assert.Equal(before, File.ReadAllBytes(ShowPath(temp, show)));
        Assert.DoesNotContain(temp.Files(), f => f.EndsWith(".saving"));
    }

    [Fact]
    public void LIB_005_a_new_file_is_never_visible_half_written()
    {
        using var temp = new TempFolder();
        var show = Samples.Show();
        var visibleDuringWrite = true;
        var lib = new FolderLibrary(temp.Path, Fast(tempPath =>
        {
            visibleDuringWrite = File.Exists(ShowPath(temp, show));
            Assert.True(File.Exists(tempPath));
            Assert.EndsWith(".json.saving", tempPath);
        }));

        Assert.Equal(SaveOutcome.Saved, lib.Shows.Save(show, null).Outcome);

        Assert.False(visibleDuringWrite, "the real file must not exist until the finished temp file replaces it");
        Assert.Equal([$"shows/{show.Id}.json"], temp.Files());
    }

    [Fact]
    public void LIB_005_a_locked_target_succeeds_once_released_within_the_retries()
    {
        using var temp = new TempFolder();
        var show = Samples.Show();
        new FolderLibrary(temp.Path).Shows.Save(show, null);

        FileStream? hold = new(ShowPath(temp, show), FileMode.Open, FileAccess.Read, FileShare.Read);
        var attempts = 0;
        var lib = new FolderLibrary(temp.Path, Fast(_ =>
        {
            if (++attempts == 3) { hold?.Dispose(); hold = null; }
        }, attempts: 5));
        var stamp = lib.Shows.Load(show.Id)!.Stamp;

        try
        {
            var result = lib.Shows.Save(show with { Title = "After the lock" }, stamp);

            Assert.Equal(SaveOutcome.Saved, result.Outcome);
            Assert.Equal(3, attempts);
        }
        finally { hold?.Dispose(); }
        Assert.Equal("After the lock", lib.Shows.Load(show.Id)!.Result.Value!.Title);
    }

    [Fact]
    public void LIB_005_a_target_that_stays_locked_gives_IoFailed_not_an_exception()
    {
        using var temp = new TempFolder();
        var show = Samples.Show();
        new FolderLibrary(temp.Path).Shows.Save(show, null);
        var lib = new FolderLibrary(temp.Path, Fast(attempts: 2));
        var stamp = lib.Shows.Load(show.Id)!.Stamp;

        using var hold = new FileStream(ShowPath(temp, show), FileMode.Open, FileAccess.Read, FileShare.Read);
        var result = lib.Shows.Save(show with { Title = "Blocked" }, stamp);

        Assert.Equal(SaveOutcome.IoFailed, result.Outcome);
        Assert.False(string.IsNullOrEmpty(result.Error));
        Assert.DoesNotContain(temp.Files(), f => f.EndsWith(".saving"));
    }

    // ---- LIB-006 ----

    [Fact]
    public void LIB_006_a_file_changed_on_disk_since_loading_is_not_overwritten()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        var show = Samples.Show();
        lib.Shows.Save(show, null);
        var loaded = lib.Shows.Load(show.Id)!;

        // Another machine saves a different version through Drive.
        new FolderLibrary(temp.Path).Shows.Save(show with { Title = "Edited elsewhere" }, loaded.Stamp);
        var diskBytes = File.ReadAllBytes(ShowPath(temp, show));

        var result = lib.Shows.Save(show with { Title = "My edit" }, loaded.Stamp);

        Assert.Equal(SaveOutcome.Conflict, result.Outcome);
        Assert.NotNull(result.DiskStamp);
        Assert.False(result.DiskStamp.SameContentAs(loaded.Stamp));
        Assert.Equal(diskBytes, File.ReadAllBytes(ShowPath(temp, show)));
    }

    [Fact]
    public void LIB_006_touching_a_file_without_changing_it_is_not_a_conflict()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        var show = Samples.Show();
        lib.Shows.Save(show, null);
        var loaded = lib.Shows.Load(show.Id)!;
        File.SetLastWriteTimeUtc(ShowPath(temp, show), DateTime.UtcNow.AddHours(1)); // a sync tool re-stamped it

        var result = lib.Shows.Save(show with { Title = "My edit" }, loaded.Stamp);

        Assert.Equal(SaveOutcome.Saved, result.Outcome);
    }

    [Fact]
    public void LIB_006_a_stale_stamp_after_our_own_save_is_a_conflict_and_the_new_stamp_is_not()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        var show = Samples.Show();
        var first = lib.Shows.Save(show, null);

        var second = lib.Shows.Save(show with { Title = "Two" }, first.Stamp);
        var staleAgain = lib.Shows.Save(show with { Title = "Three" }, first.Stamp);
        var freshAgain = lib.Shows.Save(show with { Title = "Three" }, second.Stamp);

        Assert.Equal(SaveOutcome.Saved, second.Outcome);
        Assert.Equal(SaveOutcome.Conflict, staleAgain.Outcome);
        Assert.Equal(SaveOutcome.Saved, freshAgain.Outcome);
        Assert.Equal("Three", lib.Shows.Load(show.Id)!.Result.Value!.Title);
    }

    [Fact]
    public void LIB_006_the_stamp_returned_by_a_save_matches_what_loading_the_file_gives()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        var show = Samples.Show();

        var saved = lib.Shows.Save(show, null);

        Assert.True(lib.Shows.Load(show.Id)!.Stamp!.SameContentAs(saved.Stamp));
    }

    [Fact]
    public void LIB_006_saving_a_new_object_over_an_existing_file_is_a_conflict()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        var show = Samples.Show();
        lib.Shows.Save(show, null);

        var result = lib.Shows.Save(show with { Title = "Different" }, expected: null);

        Assert.Equal(SaveOutcome.Conflict, result.Outcome);
        Assert.Equal("Amazing Grace", lib.Shows.Load(show.Id)!.Result.Value!.Title);
    }

    [Fact]
    public void LIB_006_saving_when_the_file_was_deleted_elsewhere_is_a_conflict()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        var show = Samples.Show();
        var stamp = lib.Shows.Save(show, null).Stamp;
        File.Delete(ShowPath(temp, show));

        var result = lib.Shows.Save(show with { Title = "Edit" }, stamp);

        Assert.Equal(SaveOutcome.Conflict, result.Outcome);
        Assert.Null(result.DiskStamp);
        Assert.False(File.Exists(ShowPath(temp, show)));
    }

    [Fact]
    public void LIB_006_the_same_rules_hold_for_singleton_files()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        var first = lib.Categories.Save(Samples.Categories(), null);
        var other = new CategoryList { Categories = [new Category { Id = Samples.I<Category>(99), Name = "Elsewhere" }, BuiltInCategories.Scripture] };
        new FolderLibrary(temp.Path).Categories.Save(other, first.Stamp);

        var result = lib.Categories.Save(Samples.Categories(), first.Stamp);

        Assert.Equal(SaveOutcome.Conflict, result.Outcome);
        Assert.Contains(lib.Categories.Load().Result.Value!.Categories, c => c.Name == "Elsewhere");
    }

    [Fact]
    public void LIB_006_saving_identical_content_writes_nothing()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        var show = Samples.Show();
        var saved = lib.Shows.Save(show, null);
        var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(ShowPath(temp, show), old);

        var again = lib.Shows.Save(show, saved.Stamp);

        Assert.Equal(SaveOutcome.Unchanged, again.Outcome);
        Assert.True(again.Succeeded);
        Assert.Equal(old, File.GetLastWriteTimeUtc(ShowPath(temp, show)));
    }

    [Fact]
    public void LIB_006_delete_follows_the_same_rules()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        var show = Samples.Show();
        var saved = lib.Shows.Save(show, null);
        var changed = lib.Shows.Save(show with { Title = "Changed" }, saved.Stamp);

        var stale = lib.Shows.Delete(show.Id, saved.Stamp);
        var noStamp = lib.Shows.Delete(show.Id, null);
        Assert.Equal(SaveOutcome.Conflict, stale.Outcome);
        Assert.Equal(SaveOutcome.Conflict, noStamp.Outcome);
        Assert.True(File.Exists(ShowPath(temp, show)));

        var good = lib.Shows.Delete(show.Id, changed.Stamp);
        Assert.Equal(SaveOutcome.Deleted, good.Outcome);
        Assert.False(File.Exists(ShowPath(temp, show)));

        Assert.Equal(SaveOutcome.Unchanged, lib.Shows.Delete(show.Id, changed.Stamp).Outcome); // already gone
    }
}
