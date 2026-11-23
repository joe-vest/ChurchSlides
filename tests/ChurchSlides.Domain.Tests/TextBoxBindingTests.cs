using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Resolution;
using ChurchSlides.Domain.Text;
using static ChurchSlides.Domain.Tests.Fx;

namespace ChurchSlides.Domain.Tests;

public class TextBoxBindingTests
{
    private static readonly Template Template = Tpl(1, body: new RectRelative(0.2, 0.2, 0.6, 0.5));

    [Fact]
    public void TPL_023_detaching_copies_the_current_position_so_the_box_does_not_move()
    {
        var bound = Box(Rich(Run("x")), name: "body");
        var detached = TextBoxBinding.Detach(bound, Template);

        Assert.True(detached.IsDetached);
        Assert.Equal("body", detached.TemplateBoxName);
        Assert.Equal(new RectRelative(0.2, 0.2, 0.6, 0.5), detached.Geometry);

        // Resolves to exactly the same position as before.
        RectRelative Place(SlideTextBox box) =>
            SlideResolver.Resolve(SlideOf(1, box), Ctx(ShowOf([SlideOf(1)], defaultTemplate: Template.Id), Set([Template]))).TextBoxes[0].Geometry;
        Assert.Equal(Place(bound), Place(detached));
    }

    [Fact]
    public void Detaching_an_already_detached_box_changes_nothing()
    {
        var detached = Box(Rich(Run("x")), name: "body", geometry: new RectRelative(0, 0, 1, 1));
        Assert.Same(detached, TextBoxBinding.Detach(detached, Template));
    }

    [Fact]
    public void Detaching_a_free_unplaced_box_binds_it_to_the_first_box_it_lands_in()
    {
        var detached = TextBoxBinding.Detach(Box(Rich(Run("x")), name: null), Template);

        Assert.Equal("body", detached.TemplateBoxName);
        Assert.Equal(new RectRelative(0.2, 0.2, 0.6, 0.5), detached.Geometry);
    }

    [Fact]
    public void TPL_023_reattaching_discards_the_own_geometry_and_keeps_the_template_box()
    {
        var detached = Box(Rich(Run("x")), name: "body", geometry: new RectRelative(0, 0, 0.1, 0.1));
        var reattached = TextBoxBinding.Reattach(detached, Template);

        Assert.False(reattached.IsDetached);
        Assert.Null(reattached.Geometry);
        Assert.Equal("body", reattached.TemplateBoxName);
        Assert.Equal(detached.Content, reattached.Content);
    }

    [Fact]
    public void Reattaching_a_box_whose_template_box_is_gone_binds_it_to_the_first_box()
    {
        var detached = Box(Rich(Run("x")), name: "chorus", geometry: new RectRelative(0, 0, 0.1, 0.1));
        Assert.Equal("body", TextBoxBinding.Reattach(detached, Template).TemplateBoxName);
    }

    [Fact]
    public void Detach_then_reattach_returns_to_the_original_bound_box()
    {
        var bound = Box(Rich(Run("x")), name: "body");
        Assert.Equal(bound, TextBoxBinding.Reattach(TextBoxBinding.Detach(bound, Template), Template));
    }

    // ---- TPL-026 -------------------------------------------------------------------------

    [Fact]
    public void TPL_026_a_new_slide_gets_one_empty_bound_box_per_content_box_and_none_for_copyright()
    {
        var template = Tpl(1);
        var twoBoxes = template with
        {
            Boxes = [.. template.Boxes.Prepend(new TemplateBox { Name = "title", Geometry = new RectRelative(0, 0, 1, 0.1) })],
        };

        var slide = SlideFactory.CreateBlank(twoBoxes, "Verse 1");

        Assert.Equal("Verse 1", slide.Label);
        Assert.False(slide.SkippedByDefault);
        Assert.Equal(["title", "body"], slide.TextBoxes.Select(b => b.TemplateBoxName));
        Assert.All(slide.TextBoxes, b =>
        {
            Assert.False(b.IsDetached);
            Assert.Empty(b.Content.Paragraphs);
        });
        Assert.Equal(2, slide.TextBoxes.Select(b => b.Id).Distinct().Count());
    }

    [Fact]
    public void New_slides_get_new_ids_each_time()
    {
        var a = SlideFactory.CreateBlank(Template);
        var b = SlideFactory.CreateBlank(Template);

        Assert.NotEqual(a.Id, b.Id);
        Assert.False(a.Id.IsEmpty);
    }

    // ---- TPL-025 -------------------------------------------------------------------------

    [Fact]
    public void WithoutFormatting_strips_run_formats_and_keeps_text_and_tokens()
    {
        var content = Rich(
            Run("a", new RunFormat { Bold = true }),
            Tok(new VariableToken { Name = "x" }, new RunFormat { ColorHex = "#FF0000" }));

        var cleared = content.WithoutFormatting();

        Assert.Equal(content.ToPlainText(), cleared.ToPlainText());
        Assert.All(cleared.Paragraphs[0].Inlines, i => Assert.Null(i.Format));
        Assert.IsType<TokenRun>(cleared.Paragraphs[0].Inlines[1]);
    }
}
