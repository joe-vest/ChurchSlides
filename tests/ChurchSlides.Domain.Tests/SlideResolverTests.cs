using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Resolution;
using ChurchSlides.Domain.Text;
using static ChurchSlides.Domain.Tests.Fx;

namespace ChurchSlides.Domain.Tests;

public class SlideResolverTests
{
    private static RenderModel Resolve(Slide slide, Template template, Show? show = null) =>
        SlideResolver.Resolve(slide, Ctx(show ?? ShowOf([slide], defaultTemplate: template.Id), Set([template])));

    // ---- SLD-006 background --------------------------------------------------------------

    [Fact]
    public void SLD_006_the_template_background_is_used_unless_the_slide_overrides_it()
    {
        var template = Tpl(1, background: "#112233");
        var plain = SlideOf(1, Box(Rich(Run("hi"))));
        var overridden = plain with { BackgroundOverride = new Background { Kind = MediaKind.SolidColor, ColorHex = "#FF00FF" } };

        Assert.Equal("#112233", Resolve(plain, template).Background.ColorHex);
        Assert.Equal("#FF00FF", Resolve(overridden, template).Background.ColorHex);
    }

    // ---- TPL-020..023 binding ------------------------------------------------------------

    [Fact]
    public void TPL_021_a_bound_box_takes_geometry_and_defaults_from_its_template_box()
    {
        var template = Tpl(1, bodyColor: "#ABCDEF");
        var model = Resolve(SlideOf(1, Box(Rich(Run("Amazing")))), template);

        var box = Assert.Single(model.TextBoxes);
        Assert.Equal(DefaultBodyGeometry, box.Geometry);
        var run = Assert.IsType<RenderTextRun>(Assert.Single(Assert.Single(box.Paragraphs).Runs));
        Assert.Equal("Amazing", run.Text);
        Assert.Equal("Georgia", run.Style.FontFamily);
        Assert.Equal(0.08, run.Style.FontSize);
        Assert.Equal("#ABCDEF", run.Style.ColorHex);
        Assert.Empty(model.Problems);
    }

    [Fact]
    public void Template_box_names_match_ignoring_case()
    {
        var model = Resolve(SlideOf(1, Box(Rich(Run("x")), name: "BODY")), Tpl(1));
        Assert.Empty(model.Problems);
        Assert.Equal(DefaultBodyGeometry, model.TextBoxes[0].Geometry);
    }

    [Fact]
    public void TPL_022_a_detached_box_keeps_its_own_geometry_but_still_uses_template_defaults()
    {
        var own = new RectRelative(0.0, 0.0, 0.5, 0.5);
        var model = Resolve(SlideOf(1, Box(Rich(Run("x")), name: "body", geometry: own)), Tpl(1, bodyColor: "#ABCDEF"));

        var box = model.TextBoxes[0];
        Assert.Equal(own, box.Geometry);
        Assert.Equal("#ABCDEF", ((RenderTextRun)box.Paragraphs[0].Runs[0]).Style.ColorHex);
        Assert.Empty(model.Problems);
    }

    [Fact]
    public void A_free_box_with_no_name_but_a_geometry_uses_its_position_and_the_first_boxs_defaults()
    {
        var own = new RectRelative(0.2, 0.2, 0.3, 0.3);
        var model = Resolve(SlideOf(1, Box(Rich(Run("x")), name: null, geometry: own)), Tpl(1, bodyColor: "#ABCDEF"));

        Assert.Equal(own, model.TextBoxes[0].Geometry);
        Assert.Equal("#ABCDEF", ((RenderTextRun)model.TextBoxes[0].Paragraphs[0].Runs[0]).Style.ColorHex);
        Assert.Empty(model.Problems);
    }

    [Fact]
    public void A_box_with_no_name_and_no_geometry_goes_in_the_first_box_and_is_reported()
    {
        var model = Resolve(SlideOf(1, Box(Rich(Run("x")), name: null, id: 5)), Tpl(1));

        Assert.Equal(DefaultBodyGeometry, model.TextBoxes[0].Geometry);
        Assert.Equal([new TextBoxUnplaced(Fx.I<SlideTextBox>(5))], model.Problems.ToArray());
    }

    [Fact]
    public void TPL_027_an_unknown_template_box_name_renders_in_the_first_box_keeps_content_and_is_reported()
    {
        var model = Resolve(SlideOf(1, Box(Rich(Run("Keep me")), name: "chorus", id: 7)), Tpl(1));

        var box = Assert.Single(model.TextBoxes);
        Assert.Equal(DefaultBodyGeometry, box.Geometry);
        Assert.Equal("Keep me", TextOf(box));
        Assert.Equal([new TemplateBoxNotFound("chorus", Fx.I<SlideTextBox>(7))], model.Problems.ToArray());
    }

    [Fact]
    public void The_copyright_box_can_not_be_bound_to_by_a_slide_text_box()
    {
        var model = Resolve(SlideOf(1, Box(Rich(Run("x")), name: "copyright", id: 3)), Tpl(1));

        Assert.Equal(DefaultBodyGeometry, model.TextBoxes[0].Geometry);
        Assert.Contains(new TemplateBoxNotFound("copyright", Fx.I<SlideTextBox>(3)), model.Problems.ToArray());
    }

    [Fact]
    public void A_template_with_only_a_copyright_box_still_renders_text_and_reports_it()
    {
        var onlyCopyright = new Template
        {
            Id = I<Template>(1),
            Name = "odd",
            Boxes = [new TemplateBox { Name = "c", Geometry = CopyrightGeometry, IsCopyrightBox = true }],
        };

        var model = Resolve(SlideOf(1, Box(Rich(Run("x")))), onlyCopyright);

        Assert.Equal("x", TextOf(model.TextBoxes[0]));
        Assert.Contains(new NoUsableTemplateBox(), model.Problems.ToArray());
    }

    [Fact]
    public void Per_box_alignment_and_spacing_override_the_template_defaults()
    {
        var box = Box(Rich(Run("x"))) with { HAlign = HorizontalAlign.Left, VAlign = VerticalAlign.Top, LineSpacing = 1.5 };
        var model = Resolve(SlideOf(1, box), Tpl(1));

        Assert.Equal(HorizontalAlign.Left, model.TextBoxes[0].HAlign);
        Assert.Equal(VerticalAlign.Top, model.TextBoxes[0].VAlign);
        Assert.Equal(1.5, model.TextBoxes[0].LineSpacing);

        var inherited = Resolve(SlideOf(1, Box(Rich(Run("x")))), Tpl(1)).TextBoxes[0];
        Assert.Equal(HorizontalAlign.Center, inherited.HAlign);
        Assert.Equal(1.0, inherited.LineSpacing);
    }

    // ---- TPL-024 formatting precedence ---------------------------------------------------

    [Fact]
    public void TPL_024_inline_formatting_wins_and_unset_properties_fall_back_to_the_template()
    {
        var text = Rich(
            Run("plain"),
            Run("bold", new RunFormat { Bold = true }),
            Run("styled", new RunFormat { ColorHex = "#FF0000", FontFamily = "Arial", FontSize = 0.05, Italic = true }));

        var runs = Resolve(SlideOf(1, Box(text)), Tpl(1, bodyColor: "#EEEEEE")).TextBoxes[0].Paragraphs[0].Runs.Cast<RenderTextRun>().ToArray();

        Assert.False(runs[0].Style.Bold);
        Assert.Equal("#EEEEEE", runs[0].Style.ColorHex);

        Assert.True(runs[1].Style.Bold);
        Assert.Equal("#EEEEEE", runs[1].Style.ColorHex); // only Bold was set, color still from template
        Assert.Equal("Georgia", runs[1].Style.FontFamily);

        Assert.Equal("#FF0000", runs[2].Style.ColorHex);
        Assert.Equal("Arial", runs[2].Style.FontFamily);
        Assert.Equal(0.05, runs[2].Style.FontSize);
        Assert.True(runs[2].Style.Italic);
    }

    [Fact]
    public void TPL_025_clearing_formatting_makes_the_template_apply_again()
    {
        var formatted = Rich(Run("red", new RunFormat { ColorHex = "#FF0000", Bold = true }));
        var cleared = formatted.WithoutFormatting();

        var before = (RenderTextRun)Resolve(SlideOf(1, Box(formatted)), Tpl(1)).TextBoxes[0].Paragraphs[0].Runs[0];
        var after = (RenderTextRun)Resolve(SlideOf(1, Box(cleared)), Tpl(1)).TextBoxes[0].Paragraphs[0].Runs[0];

        Assert.Equal("#FF0000", before.Style.ColorHex);
        Assert.Equal("#EEEEEE", after.Style.ColorHex);
        Assert.False(after.Style.Bold);
        Assert.Equal("red", after.Text);
    }

    // ---- TPL-003 / TPL-013 / AS-05 template changes --------------------------------------

    [Fact]
    public void TPL_003_changing_the_template_changes_the_result_without_touching_the_slide()
    {
        var slide = SlideOf(1, Box(Rich(Run("x"))));
        var show = ShowOf([slide], defaultTemplate: I<Template>(1));

        var before = SlideResolver.Resolve(slide, Ctx(show, Set([Tpl(1, background: "#111111", body: new RectRelative(0.1, 0.1, 0.8, 0.7))])));
        var after = SlideResolver.Resolve(slide, Ctx(show, Set([Tpl(1, background: "#999999", body: new RectRelative(0.3, 0.3, 0.4, 0.4))])));

        Assert.Equal("#111111", before.Background.ColorHex);
        Assert.Equal("#999999", after.Background.ColorHex);
        Assert.NotEqual(before.TextBoxes[0].Geometry, after.TextBoxes[0].Geometry);
    }

    [Fact]
    public void AS_05_changing_the_category_template_updates_hymns_but_not_detached_boxes_or_inline_red()
    {
        var detachedPosition = new RectRelative(0.0, 0.0, 0.5, 0.5);
        var bound = SlideOf(1, Box(Rich(Run("plain "), Run("red", new RunFormat { ColorHex = "#FF0000" }))));
        var detached = SlideOf(2, Box(Rich(Run("moved")), name: "body", geometry: detachedPosition));
        var hymn = ShowOf([bound, detached]); // no Show default: the Category default applies (TPL-012)

        var original = Set([Tpl(3, background: "#112233", body: new RectRelative(0.1, 0.1, 0.8, 0.7), bodyColor: "#EEEEEE")], categoryDefault: I<Template>(3));
        var changed = Set([Tpl(3, background: "#445566", body: new RectRelative(0.2, 0.3, 0.6, 0.4), bodyColor: "#00FF00")], categoryDefault: I<Template>(3));

        var boundBefore = SlideResolver.Resolve(bound, Ctx(hymn, original));
        var boundAfter = SlideResolver.Resolve(bound, Ctx(hymn, changed));
        var detachedBefore = SlideResolver.Resolve(detached, Ctx(hymn, original));
        var detachedAfter = SlideResolver.Resolve(detached, Ctx(hymn, changed));

        Assert.Equal("#445566", boundAfter.Background.ColorHex);
        Assert.Equal(new RectRelative(0.2, 0.3, 0.6, 0.4), boundAfter.TextBoxes[0].Geometry);
        Assert.NotEqual(boundBefore.TextBoxes[0].Geometry, boundAfter.TextBoxes[0].Geometry);

        Assert.Equal(detachedPosition, detachedBefore.TextBoxes[0].Geometry);
        Assert.Equal(detachedPosition, detachedAfter.TextBoxes[0].Geometry);

        var runsAfter = boundAfter.TextBoxes[0].Paragraphs[0].Runs.Cast<RenderTextRun>().ToArray();
        Assert.Equal("#00FF00", runsAfter[0].Style.ColorHex); // unformatted text follows the template
        Assert.Equal("#FF0000", runsAfter[1].Style.ColorHex); // inline red stays red
    }

    // ---- Robustness ---------------------------------------------------------------------

    [Fact]
    public void With_no_templates_at_all_a_slide_still_resolves_on_the_fallback()
    {
        var slide = SlideOf(1, Box(Rich(Run("still shows"))));
        var model = SlideResolver.Resolve(slide, Ctx(ShowOf([slide]), TemplateSet.Empty));

        Assert.Equal(TemplateResolver.Fallback.Id, model.TemplateId);
        Assert.Equal("still shows", TextOf(model.TextBoxes[0]));
        Assert.Contains(new NoTemplateAvailable(), model.Problems.ToArray());
    }

    [Fact]
    public void A_slide_with_no_text_boxes_resolves_to_background_only()
    {
        var model = Resolve(SlideOf(1), Tpl(1));
        Assert.Empty(model.TextBoxes);
        Assert.Empty(model.Problems);
    }

    [Fact]
    public void The_same_problem_is_reported_once()
    {
        var slide = SlideOf(1, Box(Rich(Run("a")), name: "x", id: 1), Box(Rich(Run("b")), name: "x", id: 1));
        Assert.Single(Resolve(slide, Tpl(1)).Problems);
    }
}
