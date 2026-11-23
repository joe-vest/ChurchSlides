using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Resolution;

namespace ChurchSlides.Domain.Tests;

public class TemplateResolverTests
{
    // Levels in TPL-010 order: 1 = Slide override, 2 = Show default, 3 = Category default, 4 = Library default.
    public static TheoryData<int> EveryCombinationOfLevels()
    {
        var data = new TheoryData<int>();
        for (var mask = 0; mask < 16; mask++) data.Add(mask);
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryCombinationOfLevels))]
    public void TPL_010_the_first_level_that_is_set_wins(int mask)
    {
        bool slideLevel = (mask & 1) != 0, showLevel = (mask & 2) != 0, categoryLevel = (mask & 4) != 0, libraryLevel = (mask & 8) != 0;
        var slide = Fx.SlideOf(1) with { TemplateOverrideId = slideLevel ? Fx.I<Template>(1) : null };
        var show = Fx.ShowOf([slide], defaultTemplate: showLevel ? Fx.I<Template>(2) : null);
        var set = Fx.Set(
            Enumerable.Range(1, 4).Select(n => Fx.Tpl(n)),
            libraryDefault: libraryLevel ? Fx.I<Template>(4) : null,
            categoryDefault: categoryLevel ? Fx.I<Template>(3) : null);

        var result = TemplateResolver.Resolve(slide, show, set);

        int? expected = slideLevel ? 1 : showLevel ? 2 : categoryLevel ? 3 : libraryLevel ? 4 : null;
        if (expected is { } n)
        {
            Assert.Equal(Fx.I<Template>(n), result.Template.Id);
            Assert.Empty(result.Problems);
        }
        else
        {
            Assert.Same(TemplateResolver.Fallback, result.Template);
            Assert.Equal([new NoTemplateAvailable()], result.Problems.ToArray());
        }
    }

    [Fact]
    public void TPL_002_a_deleted_template_falls_through_to_the_next_level_and_is_reported()
    {
        var slide = Fx.SlideOf(1) with { TemplateOverrideId = Fx.I<Template>(9) }; // doesn't exist
        var show = Fx.ShowOf([slide], defaultTemplate: Fx.I<Template>(2));
        var set = Fx.Set([Fx.Tpl(2)]);

        var result = TemplateResolver.Resolve(slide, show, set);

        Assert.Equal(Fx.I<Template>(2), result.Template.Id);
        Assert.Equal([new TemplateMissing(Fx.I<Template>(9))], result.Problems.ToArray());
    }

    [Fact]
    public void TPL_002_every_level_deleted_uses_the_fallback_and_reports_each_missing_id()
    {
        var slide = Fx.SlideOf(1) with { TemplateOverrideId = Fx.I<Template>(7) };
        var show = Fx.ShowOf([slide], defaultTemplate: Fx.I<Template>(8));
        var set = Fx.Set([], libraryDefault: Fx.I<Template>(9));

        var result = TemplateResolver.Resolve(slide, show, set);

        Assert.Same(TemplateResolver.Fallback, result.Template);
        Assert.Equal(
            [new TemplateMissing(Fx.I<Template>(7)), new TemplateMissing(Fx.I<Template>(8)), new TemplateMissing(Fx.I<Template>(9)), new NoTemplateAvailable()],
            result.Problems.ToArray());
    }

    [Fact]
    public void A_show_whose_category_is_unknown_skips_the_category_level()
    {
        var slide = Fx.SlideOf(1);
        var show = Fx.ShowOf([slide], category: Fx.I<Category>(77));
        var set = new TemplateSet([Fx.Tpl(4)], [], Fx.I<Template>(4));

        Assert.Equal(Fx.I<Template>(4), TemplateResolver.Resolve(slide, show, set).Template.Id);
    }

    [Fact]
    public void The_fallback_template_is_usable_for_slides_and_copyright()
    {
        Assert.NotNull(TemplateBoxes.FirstContent(TemplateResolver.Fallback));
        Assert.Contains(TemplateResolver.Fallback.Boxes, b => b.IsCopyrightBox);
    }
}
