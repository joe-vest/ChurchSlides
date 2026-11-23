using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Resolution;
using ChurchSlides.Domain.Text;
using static ChurchSlides.Domain.Tests.Fx;

namespace ChurchSlides.Domain.Tests;

public class TokenResolutionTests
{
    private static RenderModel Resolve(RichText content, Service? service, Show? showOverride = null, LibrarySettings? library = null)
    {
        var slide = SlideOf(1, Box(content));
        var show = showOverride ?? ShowOf([slide]);
        return SlideResolver.Resolve(slide, Ctx(show, Set([Tpl(1)], libraryDefault: I<Template>(1)), service, library));
    }

    private static Token Var(string name) => new VariableToken { Name = name };

    // ---- VAR-001..006 ---------------------------------------------------------------------

    [Fact]
    public void VAR_002_a_variable_resolves_to_the_service_value()
    {
        var service = ServiceWith([new Variable { Name = "preacher", Value = "Pastor Sam" }]);
        var model = Resolve(Rich(Run("Speaker: "), Tok(Var("preacher"))), service);

        Assert.Equal("Speaker: Pastor Sam", TextOf(model.TextBoxes[0]));
        Assert.Empty(model.Problems);
    }

    [Fact]
    public void VAR_001_variable_lookup_ignores_case()
    {
        var service = ServiceWith([new Variable { Name = "Preacher", Value = "Pastor Sam" }]);
        var model = Resolve(Rich(Tok(Var("PREACHER"))), service);

        Assert.Equal("Pastor Sam", TextOf(model.TextBoxes[0]));
    }

    [Fact]
    public void VAR_005_an_empty_variable_renders_nothing_and_is_reported()
    {
        var service = ServiceWith([new Variable { Name = "sermon_title" }]);
        var model = Resolve(Rich(Run("Title: "), Tok(Var("sermon_title"))), service);

        Assert.Equal("Title: ", TextOf(model.TextBoxes[0]));
        Assert.Equal([new VariableEmpty("sermon_title")], model.Problems.ToArray());
    }

    [Fact]
    public void VAR_005_an_undefined_variable_renders_nothing_and_is_reported()
    {
        var model = Resolve(Rich(Tok(Var("nobody"))), ServiceWith());

        Assert.Equal("", TextOf(model.TextBoxes[0]));
        Assert.Equal([new VariableUndefined("nobody")], model.Problems.ToArray());
    }

    [Fact]
    public void VAR_006_formatting_on_a_token_applies_to_its_resolved_value()
    {
        var service = ServiceWith([new Variable { Name = "preacher", Value = "Pastor Sam" }]);
        var model = Resolve(Rich(Tok(Var("preacher"), new RunFormat { Bold = true, ColorHex = "#FF0000" })), service);

        var run = (RenderTextRun)model.TextBoxes[0].Paragraphs[0].Runs[0];
        Assert.Equal("Pastor Sam", run.Text);
        Assert.True(run.Style.Bold);
        Assert.Equal("#FF0000", run.Style.ColorHex);
    }

    [Fact]
    public void VAR_003_outside_a_service_variable_and_timer_tokens_show_their_markup()
    {
        var model = Resolve(Rich(Tok(Var("preacher")), Run(" "), Tok(new TimerToken { Name = "ServiceStart" })), service: null);

        Assert.Equal("{{preacher}} {{timer:ServiceStart}}", TextOf(model.TextBoxes[0]));
        Assert.Empty(model.Problems);
    }

    // ---- Timers ---------------------------------------------------------------------------

    [Fact]
    public void A_timer_token_becomes_a_live_run_carrying_the_timers_warning_settings()
    {
        var timer = new CountdownTimer
        {
            Name = "ServiceStart",
            Type = TimerType.CountdownDuration,
            Duration = TimeSpan.FromMinutes(10),
            WarningThresholdSeconds = 60,
            WarningColorHex = "#FF0000",
        };
        var model = Resolve(Rich(Tok(new TimerToken { Name = "servicestart" }, new RunFormat { Bold = true })), ServiceWith(timers: [timer]));

        var run = Assert.IsType<RenderTimerRun>(model.TextBoxes[0].Paragraphs[0].Runs[0]);
        Assert.Equal("ServiceStart", run.TimerName);
        Assert.Equal(60, run.WarningThresholdSeconds);
        Assert.Equal("#FF0000", run.WarningColorHex);
        Assert.True(run.Style.Bold);
        Assert.Empty(model.Problems);
    }

    [Fact]
    public void An_undefined_timer_renders_nothing_and_is_reported()
    {
        var model = Resolve(Rich(Tok(new TimerToken { Name = "Ghost" })), ServiceWith());

        Assert.Equal("", TextOf(model.TextBoxes[0]));
        Assert.Equal([new TimerUndefined("Ghost")], model.Problems.ToArray());
    }

    // ---- CCLI-003 metadata tokens ---------------------------------------------------------

    [Fact]
    public void CCLI_003_show_metadata_and_church_license_tokens_resolve_even_outside_a_service()
    {
        var metadata = new SongMetadata { Authors = "John Newton", Copyright = "Public domain", CcliSongNumber = "22025" };
        var show = ShowOf([SlideOf(1)], metadata: metadata);
        var content = Rich(
            Tok(new ShowMetadataToken { Field = ShowMetadataField.Title }), Run("|"),
            Tok(new ShowMetadataToken { Field = ShowMetadataField.Authors }), Run("|"),
            Tok(new ShowMetadataToken { Field = ShowMetadataField.Copyright }), Run("|"),
            Tok(new ShowMetadataToken { Field = ShowMetadataField.Ccli }), Run("|"),
            Tok(new ChurchLicenseToken()));

        var model = Resolve(content, service: null, show, new LibrarySettings { CcliLicenseNumber = "1234567" });

        Assert.Equal("Amazing Grace|John Newton|Public domain|22025|1234567", TextOf(model.TextBoxes[0]));
    }

    [Fact]
    public void Missing_metadata_resolves_to_empty_text_without_problems()
    {
        var content = Rich(Tok(new ShowMetadataToken { Field = ShowMetadataField.Authors }), Tok(new ChurchLicenseToken()));
        var model = Resolve(content, service: null);

        Assert.Equal("", TextOf(model.TextBoxes[0]));
        Assert.Empty(model.Problems);
    }

    // ---- VAR-001 names --------------------------------------------------------------------

    [Theory]
    [InlineData("preacher", true)]
    [InlineData("sermon_title", true)]
    [InlineData("Week2", true)]
    [InlineData("_x", true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("a b", false)]
    [InlineData("a-b", false)]
    [InlineData("a.b", false)]
    [InlineData("a:b", false)]
    [InlineData("{{a}}", false)]
    [InlineData(null, false)]
    public void VAR_001_names_are_letters_digits_and_underscores(string? name, bool valid)
    {
        Assert.Equal(valid, VariableNames.IsValid(name));
    }
}
