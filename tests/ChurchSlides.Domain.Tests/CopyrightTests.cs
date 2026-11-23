using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Resolution;
using static ChurchSlides.Domain.Tests.Fx;

namespace ChurchSlides.Domain.Tests;

public class CopyrightTests
{
    private static readonly SongMetadata Newton = new()
    {
        Authors = "John Newton",
        Copyright = "Newton Co",
        CcliSongNumber = "22025",
    };

    private static RenderTextBox? CopyrightBox(RenderModel model) => model.TextBoxes.FirstOrDefault(b => b.IsCopyright);

    private static RenderModel Resolve(
        CopyrightPlacement placement, SlidePosition position, bool publicDomain, bool templateHasBox, LibrarySettings? library = null)
    {
        var slide = SlideOf(1, Box(Rich(Run("lyrics"))));
        var show = ShowOf([slide], placement: placement, metadata: Newton with { IsPublicDomain = publicDomain }, defaultTemplate: I<Template>(1));
        var set = Set([Tpl(1, copyrightBox: templateHasBox)]);
        return SlideResolver.Resolve(slide, Ctx(show, set, library: library ?? new LibrarySettings { CcliLicenseNumber = "1234567" }, position: position));
    }

    public static TheoryData<CopyrightPlacement, bool, bool, bool, bool> EveryCombination()
    {
        var data = new TheoryData<CopyrightPlacement, bool, bool, bool, bool>();
        foreach (var placement in Enum.GetValues<CopyrightPlacement>())
            foreach (var first in new[] { false, true })
                foreach (var last in new[] { false, true })
                    foreach (var publicDomain in new[] { false, true })
                        foreach (var hasBox in new[] { false, true })
                            data.Add(placement, first, last, publicDomain, hasBox);
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryCombination))]
    public void CCLI_004_005_the_notice_appears_only_where_placement_position_license_and_template_allow(
        CopyrightPlacement placement, bool isFirst, bool isLast, bool publicDomain, bool templateHasBox)
    {
        var model = Resolve(placement, new SlidePosition(isFirst, isLast), publicDomain, templateHasBox);

        var expected = templateHasBox && !publicDomain && placement switch
        {
            CopyrightPlacement.FirstSlide => isFirst,
            CopyrightPlacement.LastSlide => isLast,
            _ => false,
        };
        Assert.Equal(expected, CopyrightBox(model) is not null);
        Assert.Contains(model.TextBoxes, b => !b.IsCopyright); // the lyrics are always there
    }

    [Fact]
    public void CCLI_004_the_default_format_produces_the_expected_notice_in_the_copyright_box_style()
    {
        var model = Resolve(CopyrightPlacement.FirstSlide, new SlidePosition(true, false), publicDomain: false, templateHasBox: true);

        var box = CopyrightBox(model)!;
        Assert.Equal(
            "Amazing Grace — John Newton · © Newton Co · Used by permission · CCLI License #1234567",
            TextOf(box));
        Assert.Equal(CopyrightGeometry, box.Geometry);
        Assert.Null(box.SourceId);
        var run = (RenderTextRun)box.Paragraphs[0].Runs[0];
        Assert.Equal("#CCCCCC", run.Style.ColorHex);
        Assert.Equal(0.025, run.Style.FontSize);
    }

    [Fact]
    public void CCLI_004_a_custom_format_from_library_settings_is_used()
    {
        var library = new LibrarySettings { CopyrightFormat = "© {{show.copyright}}", CcliLicenseNumber = "1" };
        var model = Resolve(CopyrightPlacement.LastSlide, new SlidePosition(false, true), publicDomain: false, templateHasBox: true, library);

        Assert.Equal("© Newton Co", TextOf(CopyrightBox(model)!));
    }

    [Fact]
    public void An_empty_format_shows_no_notice()
    {
        var library = new LibrarySettings { CopyrightFormat = "" };
        var model = Resolve(CopyrightPlacement.FirstSlide, new SlidePosition(true, true), publicDomain: false, templateHasBox: true, library);

        Assert.Null(CopyrightBox(model));
    }

    [Fact]
    public void Empty_metadata_renders_literally_without_clean_up()
    {
        // Owner decision 7: predictable output. The pre-service check flags songs missing authors/copyright.
        var slide = SlideOf(1);
        var show = ShowOf([slide], metadata: new SongMetadata(), defaultTemplate: I<Template>(1));
        var model = SlideResolver.Resolve(slide, Ctx(show, Set([Tpl(1)]), library: new LibrarySettings { CcliLicenseNumber = "9" }, position: new SlidePosition(true, true)));

        Assert.Equal("Amazing Grace —  · ©  · Used by permission · CCLI License #9", TextOf(CopyrightBox(model)!));
    }

    // ---- Decision 4: position counts displayed slides only ---------------------------------

    [Fact]
    public void SlidePosition_ignores_skipped_slides()
    {
        var s1 = SlideOf(1);
        var s2 = SlideOf(2);
        var s3 = SlideOf(3);
        var show = ShowOf([s1, s2, s3]);
        IReadOnlyCollection<Id<Slide>> skipped = [s1.Id];

        Assert.Equal(default, SlidePosition.For(show, s1, skipped)); // a skipped slide is neither first nor last
        Assert.Equal(new SlidePosition(true, false), SlidePosition.For(show, s2, skipped));
        Assert.Equal(new SlidePosition(false, true), SlidePosition.For(show, s3, skipped));
    }

    [Fact]
    public void SlidePosition_of_the_only_displayed_slide_is_both_first_and_last()
    {
        var s1 = SlideOf(1);
        var show = ShowOf([s1]);
        Assert.Equal(new SlidePosition(true, true), SlidePosition.For(show, s1, []));
    }

    [Fact]
    public void A_skipped_first_slide_moves_the_first_slide_notice_to_the_next_displayed_slide()
    {
        var s1 = SlideOf(1, Box(Rich(Run("one"))));
        var s2 = SlideOf(2, Box(Rich(Run("two"))));
        var show = ShowOf([s1, s2], placement: CopyrightPlacement.FirstSlide, metadata: Newton, defaultTemplate: I<Template>(1));
        var set = Set([Tpl(1)]);
        IReadOnlyCollection<Id<Slide>> skipped = [s1.Id];

        var onS2 = SlideResolver.Resolve(s2, Ctx(show, set, position: SlidePosition.For(show, s2, skipped)));

        Assert.NotNull(CopyrightBox(onS2));
    }

    [Fact]
    public void A_slide_not_in_the_show_has_no_position()
    {
        var show = ShowOf([SlideOf(1)]);
        Assert.Equal(default, SlidePosition.For(show, SlideOf(99), []));
    }
}
