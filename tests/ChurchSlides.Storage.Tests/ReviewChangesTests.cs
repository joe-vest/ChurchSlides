using ChurchSlides.Domain;
using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Serialization;
using static ChurchSlides.Storage.Tests.Samples;

namespace ChurchSlides.Storage.Tests;

/// <summary>C1.3c-2: the service state file, the built-in Scripture Category and the new save validation.</summary>
public class ReviewChangesTests
{
    private static string StatePath(TempFolder temp, Id<Service> id) => temp.Combine("service-state", $"{id}.json");

    private static ServiceState LiveState(int n = 7) => new()
    {
        ServiceId = I<Service>(n),
        Mode = ServiceMode.Live,
        Dismissals = [new CheckDismissal { Rule = "VariableEmpty", Target = "preacher" }],
        LogPromptDeclined = true,
    };

    // ---- LIB-013: service state ------------------------------------------------------------

    [Fact]
    public void LIB_013_state_round_trips_in_the_service_state_folder()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);

        Assert.True(lib.ServiceStates.Save(LiveState()).Succeeded);

        Assert.Equal([$"service-state/{I<Service>(7)}.json"], temp.Files());
        var loaded = lib.ServiceStates.Load(I<Service>(7));
        Assert.Equal(LoadStatus.Ok, loaded.Status);
        Assert.Equal(ServiceMode.Live, loaded.State.Mode);
        Assert.Equal("preacher", Assert.Single(loaded.State.Dismissals).Target);
        Assert.True(loaded.State.LogPromptDeclined);
    }

    [Fact]
    public void LIB_013_a_missing_state_file_means_rehearse_with_nothing_dismissed_and_writes_nothing()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);

        var loaded = lib.ServiceStates.Load(I<Service>(7));

        Assert.Equal(LoadStatus.Ok, loaded.Status);
        Assert.Equal(ServiceState.Defaults(I<Service>(7)), loaded.State);
        Assert.False(Directory.Exists(temp.Path));
    }

    [Fact]
    public void LIB_013_a_damaged_state_file_loads_as_defaults_and_the_next_save_repairs_it()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        var path = StatePath(temp, I<Service>(7));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");

        var loaded = lib.ServiceStates.Load(I<Service>(7));
        Assert.Equal(LoadStatus.Unreadable, loaded.Status);
        Assert.Equal(ServiceMode.Rehearse, loaded.State.Mode);

        Assert.True(lib.ServiceStates.Save(LiveState()).Succeeded);
        Assert.Equal(LoadStatus.Ok, lib.ServiceStates.Load(I<Service>(7)).Status);
        Assert.Equal(ServiceMode.Live, lib.ServiceStates.Load(I<Service>(7)).State.Mode);
    }

    [Fact]
    public void LIB_013_the_last_write_wins_when_the_file_changed_elsewhere()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        lib.ServiceStates.Save(LiveState());
        new FolderLibrary(temp.Path).ServiceStates.Save(LiveState() with { LogPromptDeclined = false }); // the other machine

        var result = lib.ServiceStates.Save(LiveState() with { Mode = ServiceMode.Rehearse });

        Assert.Equal(SaveOutcome.Saved, result.Outcome);
        Assert.Equal(ServiceMode.Rehearse, lib.ServiceStates.Load(I<Service>(7)).State.Mode);
    }

    [Fact]
    public void LIB_013_unchanged_content_writes_nothing()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        lib.ServiceStates.Save(LiveState());
        var path = StatePath(temp, I<Service>(7));
        var stamp = File.GetLastWriteTimeUtc(path);
        File.SetLastWriteTimeUtc(path, stamp.AddMinutes(-5));
        var before = File.GetLastWriteTimeUtc(path);

        var result = lib.ServiceStates.Save(LiveState());

        Assert.Equal(SaveOutcome.Unchanged, result.Outcome);
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void LIB_013_a_state_file_from_a_newer_version_is_not_overwritten()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        lib.ServiceStates.Save(LiveState());
        var path = StatePath(temp, I<Service>(7));
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2"));
        var before = File.ReadAllBytes(path);

        var result = lib.ServiceStates.Save(LiveState() with { Mode = ServiceMode.Rehearse });

        Assert.Equal(SaveOutcome.ReadOnlyNewer, result.Outcome);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(LoadStatus.ReadOnlyNewer, lib.ServiceStates.Load(I<Service>(7)).Status);
    }

    [Fact]
    public void LIB_013_state_files_never_appear_in_the_services_list_and_can_be_deleted()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);
        lib.Services.Save(Service(), null);
        lib.ServiceStates.Save(LiveState());

        Assert.Single(lib.Services.LoadAll());

        Assert.Equal(SaveOutcome.Deleted, lib.ServiceStates.Delete(I<Service>(7)).Outcome);
        Assert.Equal(SaveOutcome.Unchanged, lib.ServiceStates.Delete(I<Service>(7)).Outcome);
        Assert.DoesNotContain(temp.Files(), f => f.StartsWith("service-state/"));
    }

    [Fact]
    public void LIB_013_a_state_without_a_service_id_is_invalid()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);

        var result = lib.ServiceStates.Save(new ServiceState());

        Assert.Equal(SaveOutcome.Invalid, result.Outcome);
        Assert.Empty(temp.Files());
    }

    // ---- CAT-010 ---------------------------------------------------------------------------

    [Fact]
    public void CAT_010_loading_adds_scripture_without_writing_a_file()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);

        Assert.Equal(BuiltInCategories.ScriptureId, lib.Categories.Load().Result.Value!.Categories.Single().Id);

        // an old categories.json from before the built-in Category existed
        Assert.True(lib.Categories.Save(new CategoryList { Categories = [BuiltInCategories.Scripture] }, null).Succeeded);
        File.WriteAllText(temp.Combine("categories.json"), VersionedFile.Serialize(new CategoryList()));
        var before = File.ReadAllBytes(temp.Combine("categories.json"));

        var loaded = lib.Categories.Load();

        Assert.Contains(loaded.Result.Value!.Categories, c => c.Id == BuiltInCategories.ScriptureId);
        Assert.Equal(before, File.ReadAllBytes(temp.Combine("categories.json")));
        Assert.Equal(["categories.json"], temp.Files());
    }

    [Fact]
    public void CAT_010_saving_a_list_without_scripture_is_invalid_and_a_renamed_one_is_fine()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);

        var refused = lib.Categories.Save(new CategoryList { Categories = [new Category { Id = I<Category>(2), Name = "Hymns" }] }, null);
        Assert.Equal(SaveOutcome.Invalid, refused.Outcome);
        Assert.Empty(temp.Files());

        var renamed = new CategoryList { Categories = [BuiltInCategories.Scripture with { Name = "Bible readings" }] };
        Assert.True(lib.Categories.Save(renamed, null).Succeeded);
        Assert.Equal("Bible readings", lib.Categories.Load().Result.Value!.Categories.Single().Name);
    }

    // ---- ACT-010..016: trigger validity ------------------------------------------------------

    private static TriggeredAction On(ActionTrigger trigger) => new() { Trigger = trigger, Commands = [new Normal()] };

    private static ShowReferenceItem ItemWith(ActionTrigger trigger) => new()
    {
        Id = I<ServiceItem>(1),
        ShowId = I<Show>(1),
        Actions = [On(trigger)],
    };

    public static TheoryData<string, Func<FolderLibrary, SaveResult>, string> DisallowedTriggers() => new()
    {
        { "Show with ServiceStarted", l => l.Shows.Save(Show() with { Actions = [On(new ServiceStarted())] }, null), "Show: Action 1" },
        {
            "Slide with ShowEnded",
            l => l.Shows.Save(Show() with { Slides = [Show().Slides[0] with { Actions = [On(new ShowEnded())] }] }, null),
            "slide 1: Action 1"
        },
        { "Service Item with AudioEnded", l => l.Services.Save(Service() with { Items = [ItemWith(new AudioEnded())] }, null), "Item 1: Action 1" },
        { "Service Item with ServiceStarted", l => l.Services.Save(Service() with { Items = [ItemWith(new ServiceStarted())] }, null), "Item 1: Action 1" },
        { "Service with ShowStarted", l => l.Services.Save(Service() with { Actions = [On(new ShowStarted())] }, null), "Service: Action 1" },
        { "Service with SlideDisplayed", l => l.Services.Save(Service() with { Actions = [On(new SlideDisplayed())] }, null), "Service: Action 1" },
        {
            "Service Template with AudioEnded",
            l => l.ServiceTemplates.Save(ServiceTemplate() with { Actions = [On(new AudioEnded())] }, null),
            "Service Template: Action 1"
        },
        {
            "local Show in a Service with ServiceStarted",
            l =>
            {
                var copy = (ShowCopyItem)Service().Items[1];
                return l.Services.Save(Service() with { Items = [copy with { LocalShow = copy.LocalShow with { Actions = [On(new ServiceStarted())] } }] }, null);
            },
            "local Show"
        },
    };

    [Theory]
    [MemberData(nameof(DisallowedTriggers))]
    public void ACT_010_to_016_a_trigger_that_is_not_allowed_where_it_is_refuses_the_save(
        string name, Func<FolderLibrary, SaveResult> save, string expectedPlace)
    {
        using var temp = new TempFolder();

        var result = save(new FolderLibrary(temp.Path));

        Assert.True(result.Outcome == SaveOutcome.Invalid, name);
        Assert.Contains(result.Problems!, p => p.Contains(expectedPlace) && p.Contains("isn't allowed here"));
        Assert.Empty(temp.Files());
    }

    [Fact]
    public void ACT_010_to_016_allowed_triggers_save()
    {
        using var temp = new TempFolder();
        var lib = new FolderLibrary(temp.Path);

        Assert.True(lib.Services.Save(Service() with
        {
            Items = [ItemWith(new SlideDisplayed()), ItemWith(new ShowEnded()) with { Id = I<ServiceItem>(2) }],
            Actions = [On(new ServiceStarted()), On(new TimerExpired { TimerName = "T" })],
        }, null).Succeeded);
        Assert.True(lib.Shows.Save(Show() with { Actions = [On(new AudioEnded())] }, null).Succeeded);
    }

    // ---- VAR-001, TMR-001: names -------------------------------------------------------------

    public static TheoryData<string, Func<FolderLibrary, SaveResult>, string> BadNames() => new()
    {
        {
            "duplicate Variable ignoring case",
            l => l.Services.Save(Service() with { Variables = [new Variable { Name = "Preacher" }, new Variable { Name = "preacher" }] }, null),
            "Variable \"preacher\""
        },
        {
            "duplicate Timer",
            l => l.Services.Save(Service() with { Timers = [Timer("Start"), Timer("START")] }, null),
            "Timer \"START\""
        },
        {
            "invalid Variable characters",
            l => l.Services.Save(Service() with { Variables = [new Variable { Name = "my name" }] }, null),
            "Variable \"my name\""
        },
        {
            "invalid Timer characters",
            l => l.ServiceTemplates.Save(ServiceTemplate() with { Timers = [Timer("a:b")] }, null),
            "Timer \"a:b\""
        },
        {
            "empty Variable name in a Service Template",
            l => l.ServiceTemplates.Save(ServiceTemplate() with { Variables = [new Variable { Name = "" }] }, null),
            "Variable \"\""
        },
    };

    private static CountdownTimer Timer(string name) =>
        new() { Name = name, Type = TimerType.CountdownDuration, Duration = TimeSpan.FromMinutes(1) };

    [Theory]
    [MemberData(nameof(BadNames))]
    public void VAR_001_TMR_001_invalid_or_duplicate_names_refuse_the_save(string name, Func<FolderLibrary, SaveResult> save, string expectedProblem)
    {
        using var temp = new TempFolder();

        var result = save(new FolderLibrary(temp.Path));

        Assert.True(result.Outcome == SaveOutcome.Invalid, name);
        Assert.Contains(result.Problems!, p => p.Contains(expectedProblem));
        Assert.Empty(temp.Files());
    }

    [Fact]
    public void VAR_001_a_variable_and_a_timer_may_share_a_name()
    {
        using var temp = new TempFolder();
        var service = Service() with { Variables = [new Variable { Name = "Start" }], Timers = [Timer("Start")] };

        Assert.True(new FolderLibrary(temp.Path).Services.Save(service, null).Succeeded);
    }
}
