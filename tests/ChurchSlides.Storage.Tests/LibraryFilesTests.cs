using System.Text;
using System.Text.RegularExpressions;
using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Serialization;

namespace ChurchSlides.Storage.Tests;

public class LibraryFilesTests
{
    [Fact]
    public void LIB_002_shows_round_trip_through_a_file_named_after_their_id()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        var show = Samples.Show();

        var saved = lib.Shows.Save(show, expected: null);
        var loaded = lib.Shows.Load(show.Id);

        Assert.Equal(SaveOutcome.Saved, saved.Outcome);
        Assert.Equal([$"shows/{show.Id}.json"], temp.Files());
        Assert.NotNull(loaded);
        Assert.Equal(LoadStatus.Ok, loaded.Result.Status);
        Assert.Equal(show.Title, loaded.Result.Value!.Title);
        Assert.Equal(show.Slides[0].TextBoxes[0].Content.ToPlainText(), loaded.Result.Value.Slides[0].TextBoxes[0].Content.ToPlainText());
        Assert.True(loaded.NameMatchesId);
        Assert.True(loaded.Stamp!.SameContentAs(saved.Stamp));
    }

    [Fact]
    public void LIB_002_every_kind_lands_in_its_own_place()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        var service = Samples.Service();
        var template = Samples.Template();
        var serviceTemplate = Samples.ServiceTemplate();

        Assert.True(lib.Services.Save(service, null).Succeeded);
        Assert.True(lib.Templates.Save(template, null).Succeeded);
        Assert.True(lib.ServiceTemplates.Save(serviceTemplate, null).Succeeded);
        Assert.True(lib.Categories.Save(Samples.Categories(), null).Succeeded);
        Assert.True(lib.Settings.Save(new LibrarySettings { CcliLicenseNumber = "123456" }, null).Succeeded);

        Assert.Equal(
            [
                "categories.json",
                "library.json",
                $"service-templates/{serviceTemplate.Id}.json",
                $"services/{service.Id}.json",
                $"templates/{template.Id}.json",
            ],
            temp.Files());

        Assert.Equal("Sunday", lib.Services.Load(service.Id)!.Result.Value!.Name);
        Assert.Equal(2, lib.Services.Load(service.Id)!.Result.Value!.Items.Length);
        Assert.Equal("Hymns", lib.Templates.Load(template.Id)!.Result.Value!.Name);
        Assert.Equal("Weekly", lib.ServiceTemplates.Load(serviceTemplate.Id)!.Result.Value!.Name);
        Assert.Equal(2, lib.Categories.Load().Result.Value!.Categories.Length);
        Assert.Equal("123456", lib.Settings.Load().Result.Value!.CcliLicenseNumber);
    }

    [Fact]
    public void LIB_002_LoadAll_returns_every_object_and_an_absent_folder_gives_an_empty_list()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        Assert.Empty(lib.Shows.LoadAll());
        Assert.Null(lib.Shows.Load(Samples.I<Show>(1)));

        lib.Shows.Save(Samples.Show(1, "B song"), null);
        lib.Shows.Save(Samples.Show(2, "A song"), null);

        var all = lib.Shows.LoadAll();
        Assert.Equal(2, all.Count);
        Assert.Equal(["B song", "A song"], all.Select(f => f.Result.Value!.Title)); // ordered by file name (the Id)
    }

    [Fact]
    public void LIB_002_a_missing_singleton_file_gives_defaults_and_no_stamp()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);

        var categories = lib.Categories.Load();
        var settings = lib.Settings.Load();

        Assert.Empty(categories.Result.Value!.Categories);
        Assert.Null(categories.Stamp);
        Assert.Equal(CanvasAspect.Ratio16x10, settings.Result.Value!.Canvas);
        Assert.False(Directory.Exists(temp.Path), "opening and reading must not create folders");

        Assert.True(lib.Settings.Save(settings.Result.Value, settings.Stamp).Succeeded); // null stamp = "must not exist yet"
    }

    [Fact]
    public void LIB_002_a_file_whose_name_disagrees_with_its_id_is_flagged()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        var show = Samples.Show();
        lib.Shows.Save(show, null);
        File.Copy(temp.Combine("shows", $"{show.Id}.json"), temp.Combine("shows", $"{Guid.NewGuid()}.json"));

        var all = lib.Shows.LoadAll();

        Assert.Equal(2, all.Count);
        Assert.Single(all, f => !f.NameMatchesId);
        Assert.Single(all, f => f.NameMatchesId);
    }

    [Fact]
    public void LIB_001_003_files_hold_no_absolute_paths()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        lib.Templates.Save(Samples.Template(), null); // has a media path
        lib.Shows.Save(Samples.Show(), null);
        lib.Services.Save(Samples.Service(), null);
        lib.Settings.Save(new LibrarySettings { LogoPath = ChurchSlides.Domain.LibraryRelativePath.Create("media/images/logo.png") }, null);

        foreach (var file in temp.Files())
        {
            var text = File.ReadAllText(temp.Combine(file.Split('/')));
            Assert.DoesNotContain(temp.Path, text);
            Assert.DoesNotContain("Église", text);
            Assert.False(Regex.IsMatch(text, @"[A-Za-z]:[\\/]"), $"{file} contains a drive path");
        }
        Assert.Contains("media/images/hymn-bg.png", File.ReadAllText(temp.Combine("templates", $"{Samples.I<Template>(3)}.json")));
    }

    [Fact]
    public void LIB_008_nothing_but_library_files_ever_lands_in_the_library_folder()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        lib.Shows.Save(Samples.Show(), null);
        var loaded = lib.Shows.Load(Samples.I<Show>(1))!;
        lib.Shows.Save(Samples.Show(1, "Changed"), loaded.Stamp);
        lib.Shows.Delete(Samples.I<Show>(1), lib.Shows.Load(Samples.I<Show>(1))!.Stamp);
        lib.Categories.Save(Samples.Categories(), null);
        lib.Settings.Save(new LibrarySettings(), null);

        Assert.Equal(["categories.json", "library.json"], temp.Files());
    }

    [Fact]
    public void LIB_005_files_are_utf8_without_a_byte_order_mark_and_equal_the_serialized_text()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        var show = Samples.Show();
        lib.Shows.Save(show, null);

        var bytes = File.ReadAllBytes(temp.Combine("shows", $"{show.Id}.json"));

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal(VersionedFile.Serialize(show), new UTF8Encoding(false).GetString(bytes));
        Assert.Contains("señor", Encoding.UTF8.GetString(bytes)); // accents stay readable
    }
}
