using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Resolution;
using ChurchSlides.Domain.Serialization;
using ChurchSlides.Domain.Text;
using static ChurchSlides.Domain.Tests.Fx;

namespace ChurchSlides.Domain.Tests;

/// <summary>C1.3c-1: the model changes from the FRD v0.3 review.</summary>
public class ReviewChangesTests
{
    // ---- LIB-013, MODE-001 -------------------------------------------------------------

    [Fact]
    public void LIB_013_MODE_001_a_service_file_has_no_mode_field()
    {
        var json = VersionedFile.Serialize(SampleData.Service());
        Assert.DoesNotContain("\"mode\"", json);
        Assert.DoesNotContain("defaultScriptureTemplateId", VersionedFile.Serialize(SampleData.Settings()));
    }

    [Fact]
    public void LIB_013_service_state_round_trips()
    {
        var json = VersionedFile.Serialize(SampleData.ServiceState());
        var loaded = VersionedFile.Deserialize<ServiceState>(json);

        Assert.Equal(LoadStatus.Ok, loaded.Status);
        Assert.Equal(json, VersionedFile.Serialize(loaded.Value!));
        Assert.Equal(ServiceMode.Live, loaded.Value!.Mode);
        Assert.Equal(2, loaded.Value.Dismissals.Length);
        Assert.True(loaded.Value.LogPromptDeclined);
    }

    [Fact]
    public void LIB_013_default_service_state_is_rehearse_with_nothing_dismissed()
    {
        var state = ServiceState.Defaults(I<Service>(7));
        Assert.Equal(ServiceMode.Rehearse, state.Mode);
        Assert.Empty(state.Dismissals);
        Assert.False(state.LogPromptDeclined);
    }

    // ---- ACT-033 -------------------------------------------------------------------------

    [Fact]
    public void ACT_033_the_normal_command_survives_a_round_trip_with_its_type()
    {
        var service = ServiceWith() with
        {
            Actions = [new TriggeredAction { Trigger = new ServiceStarted(), Commands = [new Normal()] }],
        };
        var loaded = VersionedFile.Deserialize<Service>(VersionedFile.Serialize(service));

        Assert.IsType<Normal>(Assert.Single(loaded.Value!.Actions[0].Commands));
        Assert.Contains("\"normal\"", VersionedFile.Serialize(service));
    }

    // ---- CAT-010 -------------------------------------------------------------------------

    [Fact]
    public void CAT_010_EnsureIn_adds_scripture_to_an_empty_list()
    {
        var list = BuiltInCategories.EnsureIn(new CategoryList());

        var scripture = Assert.Single(list.Categories);
        Assert.Equal(BuiltInCategories.ScriptureId, scripture.Id);
        Assert.Equal("Scripture", scripture.Name);
    }

    [Fact]
    public void CAT_010_EnsureIn_leaves_a_list_that_has_it_even_renamed_unchanged()
    {
        var renamed = new CategoryList { Categories = [BuiltInCategories.Scripture with { Name = "Bible readings" }] };
        Assert.Same(renamed, BuiltInCategories.EnsureIn(renamed));
    }

    [Fact]
    public void CAT_010_the_scripture_category_id_is_the_agreed_constant()
    {
        Assert.Equal("793b069a-9c4a-482c-9b09-f94a9b196401", BuiltInCategories.ScriptureId.ToString());
    }

    // ---- TMR-009 -------------------------------------------------------------------------

    private static RenderModel ResolveBox(RichText content)
    {
        var timer = new CountdownTimer { Name = "Start", Type = TimerType.CountdownDuration, Duration = TimeSpan.FromMinutes(1) };
        var show = ShowOf([SlideOf(1, Box(content))], defaultTemplate: I<Template>(1));
        return SlideResolver.Resolve(show.Slides[0], Ctx(show, Set([Tpl(1)]), ServiceWith(variables: [new Variable { Name = "v", Value = "x" }], timers: [timer])));
    }

    private static TokenRun StartTimer => Tok(new TimerToken { Name = "Start" });

    [Fact]
    public void TMR_009_a_timer_token_alone_reports_nothing_even_with_whitespace_and_empty_paragraphs()
    {
        var content = new RichText
        {
            Paragraphs =
            [
                new Paragraph { Inlines = [Run("  ")] },
                new Paragraph { Inlines = [Run(" "), StartTimer, Run("\t")] },
                new Paragraph { Inlines = [] },
            ],
        };
        Assert.Empty(ResolveBox(content).Problems);
    }

    [Fact]
    public void TMR_009_a_timer_token_with_other_text_is_reported()
    {
        var model = ResolveBox(Rich(Run("Starts in "), StartTimer));
        Assert.Equal([new TimerTokenNotAlone("Start", I<SlideTextBox>(1))], model.Problems.ToArray());
    }

    [Fact]
    public void TMR_009_two_timer_tokens_or_a_timer_plus_a_variable_are_reported()
    {
        Assert.Contains(new TimerTokenNotAlone("Start", I<SlideTextBox>(1)), ResolveBox(Rich(StartTimer, StartTimer)).Problems);
        Assert.Contains(new TimerTokenNotAlone("Start", I<SlideTextBox>(1)), ResolveBox(Rich(StartTimer, Tok(new VariableToken { Name = "v" }))).Problems);
    }

    [Fact]
    public void TMR_009_a_timer_token_in_a_second_paragraph_with_text_in_the_first_is_reported()
    {
        var content = new RichText
        {
            Paragraphs = [new Paragraph { Inlines = [Run("Coming up")] }, new Paragraph { Inlines = [StartTimer] }],
        };
        Assert.Single(ResolveBox(content).Problems.OfType<TimerTokenNotAlone>());
    }

    // ---- ACT-010..016: trigger x level ---------------------------------------------------

    public static TheoryData<ActionTrigger, ActionLevel, bool> TriggerLevels()
    {
        var data = new TheoryData<ActionTrigger, ActionLevel, bool>();
        var allowed = new Dictionary<Type, ActionLevel[]>
        {
            [typeof(ServiceStarted)] = [ActionLevel.Service, ActionLevel.ServiceTemplate],
            [typeof(ShowStarted)] = [ActionLevel.Show, ActionLevel.ServiceItem],
            [typeof(ShowEnded)] = [ActionLevel.Show, ActionLevel.ServiceItem],
            [typeof(SlideDisplayed)] = [ActionLevel.Slide, ActionLevel.ServiceItem],
            [typeof(SlideEnded)] = [ActionLevel.Slide, ActionLevel.ServiceItem],
            [typeof(TimerExpired)] = [ActionLevel.Slide, ActionLevel.Service, ActionLevel.ServiceTemplate],
            [typeof(AudioEnded)] = [ActionLevel.Show],
        };
        ActionTrigger[] triggers =
            [new ServiceStarted(), new ShowStarted(), new ShowEnded(), new SlideDisplayed(), new SlideEnded(), new TimerExpired { TimerName = "t" }, new AudioEnded()];
        foreach (var trigger in triggers)
            foreach (var level in Enum.GetValues<ActionLevel>())
                data.Add(trigger, level, allowed[trigger.GetType()].Contains(level));
        return data;
    }

    [Theory]
    [MemberData(nameof(TriggerLevels))]
    public void ACT_010_to_016_where_each_trigger_is_allowed(ActionTrigger trigger, ActionLevel level, bool expected)
    {
        Assert.Equal(expected, ActionRules.IsAllowed(trigger, level));
    }
}
