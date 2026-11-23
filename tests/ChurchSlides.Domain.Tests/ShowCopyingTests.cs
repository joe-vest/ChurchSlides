using System.Text.RegularExpressions;
using ChurchSlides.Domain.Editing;
using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Serialization;
using static ChurchSlides.Domain.Tests.Fx;

namespace ChurchSlides.Domain.Tests;

public class ShowCopyingTests
{
    private static ShowReferenceItem ReferenceTo(Show show, params Id<Slide>[] skipped) => new()
    {
        Id = I<ServiceItem>(100),
        ShowId = show.Id,
        SkippedSlideIds = [.. skipped],
        TemplateOverrideId = I<Template>(4),
        FilledFrom = new PlaceholderSpec { Label = "Opening song" },
        Actions = [new TriggeredAction { Trigger = new ShowEnded(), Commands = [new GoToItem { ItemId = I<ServiceItem>(101) }] }],
    };

    private static IEnumerable<Guid> InternalIds(Show show) =>
        show.Slides.Select(s => s.Id.Value)
            .Concat(show.Slides.SelectMany(s => s.TextBoxes).Select(t => t.Id.Value))
            .Append(show.Id.Value);

    // ---- Fork (SVC-022, SVC-023, AS-06) ---------------------------------------------------

    [Fact]
    public void SVC_022_fork_keeps_item_slide_and_text_box_ids_and_gives_the_local_show_a_new_id()
    {
        var catalog = SampleData.Song();
        var reference = ReferenceTo(catalog, SampleData.Chorus);

        var copy = ShowCopying.Fork(reference, catalog);

        Assert.Equal(reference.Id, copy.Id);
        Assert.NotEqual(catalog.Id, copy.LocalShow.Id);
        Assert.False(copy.LocalShow.Id.IsEmpty);
        Assert.Equal(catalog.Id, copy.OriginShowId);
        Assert.Equal(catalog.Slides.Select(s => s.Id), copy.LocalShow.Slides.Select(s => s.Id));
        Assert.Equal(
            catalog.Slides.SelectMany(s => s.TextBoxes).Select(t => t.Id),
            copy.LocalShow.Slides.SelectMany(s => s.TextBoxes).Select(t => t.Id));
        Assert.Equal(catalog.Title, copy.LocalShow.Title);
    }

    [Fact]
    public void SVC_022_fork_keeps_skips_placeholder_memory_template_override_and_actions()
    {
        var catalog = SampleData.Song();
        var reference = ReferenceTo(catalog, SampleData.Chorus);

        var copy = ShowCopying.Fork(reference, catalog);

        Assert.Equal([SampleData.Chorus], copy.SkippedSlideIds.ToArray());
        Assert.Equal("Opening song", copy.FilledFrom!.Label);
        Assert.Equal(reference.TemplateOverrideId, copy.TemplateOverrideId);
        Assert.Equal(reference.Actions, copy.Actions);
    }

    [Fact]
    public void AS_06_a_fork_leaves_the_catalog_show_untouched()
    {
        var catalog = SampleData.Song();
        var before = VersionedFile.Serialize(catalog);

        _ = ShowCopying.Fork(ReferenceTo(catalog), catalog);

        Assert.Equal(before, VersionedFile.Serialize(catalog));
    }

    [Fact]
    public void Fork_refuses_a_show_the_reference_does_not_point_at()
    {
        var reference = ReferenceTo(SampleData.Song());
        Assert.Throws<ArgumentException>(() => ShowCopying.Fork(reference, SampleData.SmallPresentation()));
    }

    // ---- Revert (SVC-024) -----------------------------------------------------------------

    [Fact]
    public void SVC_024_revert_points_back_at_the_origin_and_keeps_the_items_settings()
    {
        var catalog = SampleData.Song();
        var copy = ShowCopying.Fork(ReferenceTo(catalog, SampleData.Chorus), catalog);

        Assert.True(ShowCopying.CanRevert(copy));
        var reverted = ShowCopying.Revert(copy, catalog);

        Assert.Equal(catalog.Id, reverted.ShowId);
        Assert.Equal(copy.Id, reverted.Id);
        Assert.Equal([SampleData.Chorus], reverted.SkippedSlideIds.ToArray());
        Assert.Equal("Opening song", reverted.FilledFrom!.Label);
        Assert.Equal(copy.TemplateOverrideId, reverted.TemplateOverrideId);
        Assert.Equal(copy.Actions, reverted.Actions);
    }

    [Fact]
    public void SVC_024_revert_drops_skips_for_slides_deleted_from_the_catalog_show()
    {
        var catalog = SampleData.Song();
        var copy = ShowCopying.Fork(ReferenceTo(catalog, SampleData.Chorus, SampleData.Verse2), catalog);
        var catalogLater = catalog with { Slides = [.. catalog.Slides.Where(s => s.Id != SampleData.Chorus)] };

        var reverted = ShowCopying.Revert(copy, catalogLater);

        Assert.Equal([SampleData.Verse2], reverted.SkippedSlideIds.ToArray());
    }

    [Fact]
    public void SVC_024_a_copy_without_an_origin_cannot_be_reverted()
    {
        var copy = new ShowCopyItem { Id = I<ServiceItem>(1), LocalShow = SampleData.SmallPresentation(), OriginShowId = null };

        Assert.False(ShowCopying.CanRevert(copy));
        Assert.Throws<InvalidOperationException>(() => ShowCopying.Revert(copy, SampleData.SmallPresentation()));
    }

    [Fact]
    public void Revert_refuses_a_show_that_is_not_the_origin()
    {
        var catalog = SampleData.Song();
        var copy = ShowCopying.Fork(ReferenceTo(catalog), catalog);

        Assert.Throws<InvalidOperationException>(() => ShowCopying.Revert(copy, SampleData.SmallPresentation()));
    }

    // ---- Duplicate (DEC-005) --------------------------------------------------------------

    private static Show SongWithInternalReferences()
    {
        var song = SampleData.Song();
        var verse1 = song.Slides[0] with
        {
            Actions =
            [
                new TriggeredAction
                {
                    Trigger = new SlideDisplayed(),
                    Commands =
                    [
                        new GoToSlide { Target = new SlideTarget(SampleData.Verse2) },
                        new GoToItem { ItemId = I<ServiceItem>(500) }, // names a Service Item: not ours to change
                    ],
                },
            ],
        };
        return song with { Slides = song.Slides.SetItem(0, verse1) };
    }

    [Fact]
    public void DEC_005_a_duplicate_shares_no_show_slide_or_text_box_id_with_the_original()
    {
        var original = SongWithInternalReferences();
        var copy = ShowCopying.Duplicate(original);

        var copyJson = VersionedFile.Serialize(copy);
        foreach (var id in InternalIds(original))
            Assert.DoesNotContain(id.ToString(), copyJson);

        // Only IDs of things outside the Show remain: Category and Templates.
        Assert.Contains(original.CategoryId.ToString(), copyJson);
        Assert.Contains(original.DefaultTemplateId!.Value.ToString(), copyJson);
        Assert.Contains(original.Slides[1].TemplateOverrideId!.Value.ToString(), copyJson);
        Assert.Contains(I<ServiceItem>(500).ToString(), copyJson);

        // Every ID in the copy is unique.
        var guids = Regex.Matches(copyJson, @"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}").Select(m => m.Value);
        Assert.NotEmpty(guids);
        Assert.Equal(InternalIds(copy).Count(), InternalIds(copy).Distinct().Count());
    }

    [Fact]
    public void DEC_005_cues_and_actions_that_point_at_the_shows_own_slides_follow_the_new_ids()
    {
        var original = SongWithInternalReferences();
        var copy = ShowCopying.Duplicate(original);

        Assert.Equal(copy.Slides[1].Id, copy.Audio!.Cues[0].SlideId); // Chorus cue
        Assert.Equal(copy.Slides[2].Id, copy.Audio.Cues[1].SlideId); // Verse 2 cue

        var commands = copy.Slides[0].Actions[0].Commands;
        Assert.Equal(copy.Slides[2].Id, Assert.IsType<GoToSlide>(commands[0]).Target.SlideId);
        Assert.Equal(I<ServiceItem>(500), Assert.IsType<GoToItem>(commands[1]).ItemId);
    }

    [Fact]
    public void DEC_005_a_duplicate_keeps_content_marks_and_audio_settings_and_leaves_the_original_alone()
    {
        var original = SongWithInternalReferences();
        var before = VersionedFile.Serialize(original);

        var copy = ShowCopying.Duplicate(original);

        Assert.Equal(before, VersionedFile.Serialize(original));
        Assert.Equal(original.Title, copy.Title);
        Assert.Equal(original.Slides.Select(s => s.Label), copy.Slides.Select(s => s.Label));
        Assert.Equal(original.Slides.Select(s => s.SkippedByDefault), copy.Slides.Select(s => s.SkippedByDefault));
        Assert.Equal(original.Audio!.TrimStart, copy.Audio!.TrimStart);
        Assert.Equal(original.Audio.Cues.Select(c => c.Position), copy.Audio.Cues.Select(c => c.Position));
        Assert.Equal(original.Slides[0].TextBoxes[0].Content, copy.Slides[0].TextBoxes[0].Content);
    }

    [Fact]
    public void Two_duplicates_of_the_same_show_do_not_share_ids()
    {
        var original = SampleData.Song();
        var a = ShowCopying.Duplicate(original);
        var b = ShowCopying.Duplicate(original);

        Assert.Empty(InternalIds(a).Intersect(InternalIds(b)));
    }

    [Fact]
    public void Duplicating_a_show_without_audio_or_slides_works()
    {
        var empty = ShowOf([]);
        var copy = ShowCopying.Duplicate(empty);

        Assert.NotEqual(empty.Id, copy.Id);
        Assert.Empty(copy.Slides);
        Assert.Null(copy.Audio);
    }
}
