using System.Security.Cryptography;
using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Serialization;
using ChurchSlides.Storage.Local;

namespace ChurchSlides.Storage.Tests;

/// <summary>LIB-008 (machine-local data) and SAFE-001/002 (the draft store).</summary>
public class LocalDataTests
{
    private static string Fingerprint(TempFolder folder) =>
        string.Join("|", folder.Files().Select(f =>
            f + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(folder.Combine(f.Split('/')))))));

    // ---- LIB-008 ----

    [Fact]
    public void LIB_008_the_default_local_folder_is_under_local_app_data_and_every_path_is_inside_it()
    {
        var paths = LocalDataPaths.Default();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.Equal(Path.Combine(localAppData, "ChurchSlides"), paths.Root);
        foreach (var path in new[]
        {
            paths.SettingsFile, paths.ImageCacheFolder, paths.BibleCacheFolder, paths.SearchIndexFile,
            paths.DraftsFolder, paths.LogFile, paths.LastSessionFile,
        })
            Assert.StartsWith(paths.Root + Path.DirectorySeparatorChar, path);
    }

    [Fact]
    public void LIB_008_settings_round_trip_and_keep_the_library_path_with_spaces_and_accents()
    {
        using var temp = new TempFolder();
        var store = new LocalSettingsStore(new LocalDataPaths(temp.Path));
        var settings = new LocalSettings { LibraryPath = @"G:\Mon Drive\Église\Library" };

        var saved = store.Save(settings);

        Assert.Equal(SaveOutcome.Saved, saved.Outcome);
        Assert.Equal(settings, store.Load());
        Assert.Equal(["settings.json"], temp.Files());
    }

    [Fact]
    public void LIB_008_a_missing_settings_file_gives_defaults_and_creates_nothing()
    {
        using var temp = new TempFolder();

        var loaded = new LocalSettingsStore(new LocalDataPaths(temp.Path)).Load();

        Assert.Null(loaded.LibraryPath);
        Assert.False(Directory.Exists(temp.Path));
    }

    [Theory]
    [InlineData("this is { not json")]
    [InlineData("")]
    [InlineData("[1, 2, 3]")]
    public void LIB_008_a_damaged_settings_file_gives_defaults_and_does_not_throw(string content)
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(temp.Path);
        File.WriteAllText(temp.Combine("settings.json"), content);
        var store = new LocalSettingsStore(new LocalDataPaths(temp.Path));

        Assert.Null(store.Load().LibraryPath);
        Assert.Equal(SaveOutcome.Saved, store.Save(new LocalSettings { LibraryPath = "D:\\Lib" }).Outcome); // and can be repaired
        Assert.Equal("D:\\Lib", store.Load().LibraryPath);
    }

    // ---- SAFE-001 / SAFE-002 ----

    [Fact]
    public void SAFE_002_saving_a_draft_leaves_the_library_byte_identical()
    {
        using var library = new TempFolder();
        using var local = new TempFolder();
        var lib = new FolderLibrary(library.Path);
        var show = Samples.Show();
        lib.Shows.Save(show, null);
        lib.Categories.Save(Samples.Categories(), null);
        var before = Fingerprint(library);
        var drafts = new FileDraftStore(new LocalDataPaths(local.Path));

        var result = drafts.Save(DraftKind.Show, show.Id.Value, VersionedFile.Serialize(show with { Title = "Half edited" }), lib.Shows.Load(show.Id)!.Stamp);

        Assert.Equal(SaveOutcome.Saved, result.Outcome);
        Assert.Equal(before, Fingerprint(library));
        Assert.Equal([$"drafts/show/{show.Id}.json"], local.Files());
        Assert.DoesNotContain(local.Path, library.Path); // sanity: two separate roots
    }

    [Fact]
    public void SAFE_002_a_draft_round_trips_lists_and_discards()
    {
        using var local = new TempFolder();
        var drafts = new FileDraftStore(new LocalDataPaths(local.Path));
        var showId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var text = VersionedFile.Serialize(Samples.Show() with { Title = "Señor — draft" });
        var stamp = new FileStamp(new DateTime(2026, 11, 23, 8, 0, 0, DateTimeKind.Utc), "ABC123");

        drafts.Save(DraftKind.Show, showId, text, stamp);
        drafts.Save(DraftKind.Service, serviceId, "service text", null);

        var loaded = drafts.Load(DraftKind.Show, showId)!;
        Assert.Equal(text, loaded.Text);
        Assert.Equal(stamp, loaded.BaseStamp);
        Assert.Equal(DraftKind.Show, loaded.Kind);
        Assert.Equal(showId, loaded.Id);
        Assert.True((DateTime.UtcNow - loaded.SavedUtc).Duration() < TimeSpan.FromMinutes(5));
        Assert.Null(drafts.Load(DraftKind.Service, showId)); // same Id, other kind

        var listing = drafts.List();
        Assert.Empty(listing.Problems);
        Assert.Equal(2, listing.Drafts.Count);
        Assert.Null(listing.Drafts.Single(d => d.Kind == DraftKind.Service).BaseStamp);

        Assert.Equal(SaveOutcome.Deleted, drafts.Discard(DraftKind.Show, showId).Outcome);
        Assert.Null(drafts.Load(DraftKind.Show, showId));
        Assert.Single(drafts.List().Drafts);
        Assert.True(drafts.Discard(DraftKind.Show, showId).Succeeded); // already gone
    }

    [Fact]
    public void SAFE_002_saving_a_draft_again_replaces_the_first()
    {
        using var local = new TempFolder();
        var drafts = new FileDraftStore(new LocalDataPaths(local.Path));
        var id = Guid.NewGuid();

        drafts.Save(DraftKind.Template, id, "first", null);
        drafts.Save(DraftKind.Template, id, "second", null);

        Assert.Equal("second", drafts.Load(DraftKind.Template, id)!.Text);
        Assert.Single(drafts.List().Drafts);
        Assert.DoesNotContain(local.Files(), f => f.EndsWith(".saving"));
    }

    [Fact]
    public void SAFE_002_a_damaged_draft_is_reported_not_thrown_and_other_drafts_still_list()
    {
        using var local = new TempFolder();
        var drafts = new FileDraftStore(new LocalDataPaths(local.Path));
        var good = Guid.NewGuid();
        var bad = Guid.NewGuid();
        drafts.Save(DraftKind.Show, good, "fine", null);
        drafts.Save(DraftKind.Show, bad, "will be damaged", null);
        File.WriteAllText(local.Combine("drafts", "show", $"{bad}.json"), "{ nope");
        File.WriteAllText(local.Combine("drafts", "show", "not-an-id.json"), "{}");
        File.WriteAllText(local.Combine("drafts", "show", $"{Guid.NewGuid()}.json.saving"), "half"); // crash leftover

        var listing = drafts.List();

        Assert.Equal(good, Assert.Single(listing.Drafts).Id);
        Assert.Equal(2, listing.Problems.Count);
        Assert.Contains(listing.Problems, p => p.Contains(bad.ToString()));
        Assert.Contains(listing.Problems, p => p.Contains("not-an-id.json"));
        Assert.Null(drafts.Load(DraftKind.Show, bad));
    }

    [Fact]
    public void SAFE_002_listing_with_no_drafts_folder_is_empty()
    {
        using var local = new TempFolder();

        var listing = new FileDraftStore(new LocalDataPaths(local.Path)).List();

        Assert.Empty(listing.Drafts);
        Assert.Empty(listing.Problems);
        Assert.False(Directory.Exists(local.Path));
    }

    [Fact]
    public void SAFE_001_a_draft_remembers_its_base_so_a_caller_can_tell_the_library_file_moved_on()
    {
        using var library = new TempFolder();
        using var local = new TempFolder();
        var lib = new FolderLibrary(library.Path);
        var show = Samples.Show();
        var started = lib.Shows.Save(show, null).Stamp;
        var drafts = new FileDraftStore(new LocalDataPaths(local.Path));
        drafts.Save(DraftKind.Show, show.Id.Value, VersionedFile.Serialize(show with { Title = "My draft" }), started);

        var stillCurrent = drafts.Load(DraftKind.Show, show.Id.Value)!;
        Assert.True(lib.Shows.Load(show.Id)!.Stamp!.SameContentAs(stillCurrent.BaseStamp));

        lib.Shows.Save(show with { Title = "Edited on the other machine" }, started); // the Library file moves on

        var restored = drafts.Load(DraftKind.Show, show.Id.Value)!;
        Assert.False(lib.Shows.Load(show.Id)!.Stamp!.SameContentAs(restored.BaseStamp));
        Assert.Equal("My draft", VersionedFile.Deserialize<Show>(restored.Text).Value!.Title); // the text is a normal Library file body
    }
}
