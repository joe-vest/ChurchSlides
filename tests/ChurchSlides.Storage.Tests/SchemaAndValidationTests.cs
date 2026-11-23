using System.Text.Json.Nodes;
using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Serialization;
using ChurchSlides.Storage.Migration;

namespace ChurchSlides.Storage.Tests;

/// <summary>LIB-004 (versions and migration) and the save-time validation from the C1.3 plan, decision 6.</summary>
public class SchemaAndValidationTests
{
    private static string ShowPath(TempFolder temp, Show show) => temp.Combine("shows", $"{show.Id}.json");

    private static void MakeNewer(string path, Func<string, string>? alsoChange = null)
    {
        var text = File.ReadAllText(path);
        Assert.Contains("\"schemaVersion\": 1", text);
        text = text.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2");
        File.WriteAllText(path, alsoChange?.Invoke(text) ?? text);
    }

    // ---- LIB-004 ----

    [Fact]
    public void LIB_004_a_newer_file_loads_read_only_and_is_never_overwritten_or_deleted()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        var show = Samples.Show();
        lib.Shows.Save(show, null);
        MakeNewer(ShowPath(temp, show), t => t.Replace("\"title\"", "\"aFieldFromTheFuture\": 5,\n    \"title\""));
        var before = File.ReadAllBytes(ShowPath(temp, show));

        var loaded = lib.Shows.Load(show.Id)!;
        var save = lib.Shows.Save(show with { Title = "Changed" }, loaded.Stamp);
        var delete = lib.Shows.Delete(show.Id, loaded.Stamp);

        Assert.Equal(LoadStatus.ReadOnlyNewer, loaded.Result.Status);
        Assert.True(loaded.Result.IsReadOnly);
        Assert.Equal(show.Title, loaded.Result.Value!.Title);
        Assert.Equal(SaveOutcome.ReadOnlyNewer, save.Outcome);
        Assert.Equal(SaveOutcome.ReadOnlyNewer, delete.Outcome);
        Assert.Equal(before, File.ReadAllBytes(ShowPath(temp, show)));
    }

    [Fact]
    public void LIB_004_a_newer_file_blocks_a_save_even_when_the_caller_passes_no_stamp()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        var show = Samples.Show();
        lib.Shows.Save(show, null);
        MakeNewer(ShowPath(temp, show));

        Assert.Equal(SaveOutcome.ReadOnlyNewer, lib.Shows.Save(show, expected: null).Outcome);
    }

    [Fact]
    public void LIB_004_a_damaged_file_is_unreadable_names_the_file_and_does_not_stop_LoadAll()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        lib.Shows.Save(Samples.Show(1), null);
        lib.Shows.Save(Samples.Show(2, "Second"), null);
        var bad = temp.Combine("shows", $"{Guid.NewGuid()}.json");
        File.WriteAllText(bad, "this is { not json");

        var all = lib.Shows.LoadAll();

        Assert.Equal(3, all.Count);
        var damaged = Assert.Single(all, f => f.Result.Status == LoadStatus.Unreadable);
        Assert.Null(damaged.Result.Value);
        Assert.Contains(Path.GetFileName(bad), damaged.Result.Error);
        Assert.Equal(2, all.Count(f => f.Result.Status == LoadStatus.Ok));
    }

    [Fact]
    public void LIB_004_a_file_missing_a_required_field_is_unreadable_not_a_crash()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        var show = Samples.Show();
        lib.Shows.Save(show, null);
        File.WriteAllText(ShowPath(temp, show), "{ \"schemaVersion\": 1, \"data\": { \"id\": \"" + show.Id + "\" } }");

        var loaded = lib.Shows.Load(show.Id)!;

        Assert.Equal(LoadStatus.Unreadable, loaded.Result.Status);
        Assert.Equal(SaveOutcome.Saved, lib.Shows.Save(show, loaded.Stamp).Outcome); // a damaged file can be replaced by a deliberate save
    }

    [Fact]
    public void LIB_004_a_single_migration_step_can_add_a_newly_required_field()
    {
        var chain = new MigrationChain([
            new MigrationStep<Show>(1, data => data["newlyRequired"] = "default"),
        ]);
        var envelope = Envelope(1, new JsonObject { ["title"] = "Old song" });

        var outcome = chain.Migrate(envelope, typeof(Show), 1, 2);

        Assert.True(outcome.Ok);
        Assert.Equal(2, (int)envelope["schemaVersion"]!);
        Assert.Equal("default", (string)envelope["data"]!["newlyRequired"]!);
        Assert.Equal("Old song", (string)envelope["data"]!["title"]!);
    }

    [Fact]
    public void LIB_004_steps_run_in_order_from_the_files_version_only()
    {
        var log = new List<string>();
        var chain = new MigrationChain([
            new MigrationStep<Show>(1, _ => log.Add("show 1->2")),
            new MigrationStep<Show>(2, _ => log.Add("show 2->3")),
            new MigrationStep<Show>(3, _ => log.Add("show 3->4")),
            new MigrationStep<Service>(1, _ => log.Add("service 1->2")),
        ]);
        var envelope = Envelope(2, new JsonObject());

        var outcome = chain.Migrate(envelope, typeof(Show), 2, 4);

        Assert.True(outcome.Ok);
        Assert.Equal(["show 2->3", "show 3->4"], log);
        Assert.Equal(4, (int)envelope["schemaVersion"]!);
    }

    [Fact]
    public void LIB_004_a_gap_in_the_chain_fails_and_names_the_missing_step()
    {
        var chain = new MigrationChain([new MigrationStep<Show>(1, _ => { }), new MigrationStep<Show>(3, _ => { })]);
        var envelope = Envelope(1, new JsonObject());

        var outcome = chain.Migrate(envelope, typeof(Show), 1, 4);

        Assert.False(outcome.Ok);
        Assert.Contains("version 2 to 3", outcome.Error);
    }

    [Fact]
    public void LIB_004_two_steps_for_the_same_version_are_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new MigrationChain([new MigrationStep<Show>(1, _ => { }), new MigrationStep<Show>(1, _ => { })]));
    }

    [Fact]
    public void LIB_004_a_file_without_a_data_section_cannot_be_migrated()
    {
        var outcome = new MigrationChain().Migrate(new JsonObject { ["schemaVersion"] = 1 }, typeof(Show), 1, 2);
        Assert.False(outcome.Ok);
    }

    private static JsonObject Envelope(int version, JsonObject data) => new() { ["schemaVersion"] = version, ["data"] = data };

    // ---- Validation (plan decision 6) ----

    public static TheoryData<string, Func<FolderLibrary, SaveResult>, string> InvalidSaves() => new()
    {
        { "empty Show Id", l => l.Shows.Save(Samples.Show() with { Id = default }, null), "Show: the Id is empty" },
        { "blank Show title", l => l.Shows.Save(Samples.Show(title: "  "), null), "title is empty" },
        {
            "empty Slide Id",
            l => l.Shows.Save(Samples.Show() with { Slides = [Samples.Show().Slides[0] with { Id = default }] }, null),
            "slide 1 has an empty Id"
        },
        {
            "empty Text Box Id deep inside",
            l =>
            {
                var slide = Samples.Show().Slides[0];
                return l.Shows.Save(Samples.Show() with { Slides = [slide with { TextBoxes = [slide.TextBoxes[0] with { Id = default }] }] }, null);
            },
            "slide 1, text box 1 has an empty Id"
        },
        { "empty Service Id", l => l.Services.Save(Samples.Service() with { Id = default }, null), "Service: the Id is empty" },
        { "blank Service name", l => l.Services.Save(Samples.Service(name: ""), null), "Service: the name is empty" },
        {
            "empty Service Item Id",
            l => l.Services.Save(Samples.Service() with { Items = [Samples.Service().Items[0] with { Id = default }] }, null),
            "Item 1: the Id is empty"
        },
        {
            "empty Id inside a local Show copy",
            l =>
            {
                var copy = (ShowCopyItem)Samples.Service().Items[1];
                var broken = copy with { LocalShow = copy.LocalShow with { Id = default } };
                return l.Services.Save(Samples.Service() with { Items = [broken] }, null);
            },
            "Item 1 (local Show): the Id is empty"
        },
        { "empty Template Id", l => l.Templates.Save(Samples.Template() with { Id = default }, null), "Template: the Id is empty" },
        { "blank Template name", l => l.Templates.Save(Samples.Template(name: " "), null), "Template: the name is empty" },
        { "blank Service Template name", l => l.ServiceTemplates.Save(Samples.ServiceTemplate(name: ""), null), "Service Template: the name is empty" },
        {
            "empty Service Template item Id",
            l => l.ServiceTemplates.Save(Samples.ServiceTemplate() with { Items = [Samples.ServiceTemplate().Items[0] with { Id = default }] }, null),
            "Item 1: the Id is empty"
        },
        {
            "blank Category name",
            l => l.Categories.Save(new CategoryList { Categories = [new Category { Id = Samples.I<Category>(1), Name = "" }] }, null),
            "Category 1: the name is empty"
        },
        {
            "empty Category Id",
            l => l.Categories.Save(new CategoryList { Categories = [new Category { Name = "Hymns" }] }, null),
            "Category 1: the Id is empty"
        },
    };

    [Theory]
    [MemberData(nameof(InvalidSaves))]
    public void Decision_6_invalid_objects_are_refused_and_nothing_is_written(
        string name, Func<FolderLibrary, SaveResult> save, string expectedProblem)
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);

        var result = save(lib);

        Assert.Equal(SaveOutcome.Invalid, result.Outcome);
        Assert.Contains(result.Problems!, p => p.Contains(expectedProblem));
        Assert.False(result.Succeeded);
        Assert.Empty(temp.Files());
        Assert.False(Directory.Exists(temp.Path), $"'{name}' must not even create folders");
    }

    [Fact]
    public void Decision_6_every_problem_is_listed_not_just_the_first()
    {
        var show = Samples.Show(title: "") with { Id = default };

        var problems = SaveValidation.Validate(show);

        Assert.Equal(2, problems.Count);
    }

    [Fact]
    public void Decision_6_a_valid_Service_with_a_local_copy_passes()
    {
        Assert.Empty(SaveValidation.Validate(Samples.Service()));
        Assert.Empty(SaveValidation.Validate(Samples.Show()));
        Assert.Empty(SaveValidation.Validate(Samples.Template()));
        Assert.Empty(SaveValidation.Validate(Samples.ServiceTemplate()));
        Assert.Empty(SaveValidation.Validate(Samples.Categories()));
    }

    [Fact]
    public void Decision_7_schema_versions_are_still_all_one_so_no_real_migration_runs_yet()
    {
        // When this fails, a kind has been bumped: add its migration step and an end-to-end load test (STATUS follow-up).
        Assert.Equal(1, SchemaVersions.Current<Show>());
        Assert.Equal(1, SchemaVersions.Current<Service>());
        Assert.Equal(1, SchemaVersions.Current<Template>());
        Assert.Equal(1, SchemaVersions.Current<ServiceTemplate>());
        Assert.Equal(1, SchemaVersions.Current<CategoryList>());
        Assert.Equal(1, SchemaVersions.Current<LibrarySettings>());
    }
}
