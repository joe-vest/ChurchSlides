using ChurchSlides.Domain.Editing;
using ChurchSlides.Domain.Model;
using static ChurchSlides.Domain.Tests.Fx;

namespace ChurchSlides.Domain.Tests;

public class SkipRulesTests
{
    private static Show CuedSong() => Verses(4) with
    {
        Audio = new AudioTrack
        {
            MediaPath = LibraryRelativePath.Create("media/audio/song.mp3"),
            Cues = [new Cue(S(2), TimeSpan.FromSeconds(10))],
        },
    };

    private static Id<Slide>[] Skipped(ShowItem item) => [.. item.SkippedSlideIds];

    private static ShowItem Apply(ShowItem item, Show show, int slide, bool skipped) =>
        SkipRules.SetSkipped(item, show, S(slide), skipped).Value;

    // ---- SVC-027 / SVC-028: the 8-verse example ---------------------------------------------

    [Fact]
    public void SVC_027_the_eight_verse_example_end_to_end()
    {
        var catalog = Verses(8, marked: [5, 6]);

        // Adding the song starts the Service with 5 and 6 skipped.
        ShowItem item = ShowItems.AddReference(catalog);
        Assert.Equal([S(5), S(6)], Skipped(item));

        // In the Service: un-skip 5 and 6, skip 3 and 4.
        item = Apply(item, catalog, 5, false);
        item = Apply(item, catalog, 6, false);
        item = Apply(item, catalog, 3, true);
        item = Apply(item, catalog, 4, true);
        Assert.Equal([S(3), S(4)], Skipped(item));

        // The catalog is untouched.
        Assert.Equal([5, 6], Enumerable.Range(1, 8).Where(n => catalog.Slides[n - 1].SkippedByDefault));

        // Changing the catalog marks later doesn't change the Service Item.
        var catalogLater = SkipRules.SetSkippedByDefault(catalog, S(1), true).Value;
        Assert.Equal([S(3), S(4)], Skipped(item));

        // "Reset to catalog defaults" re-copies the current marks (SVC-028).
        var reset = SkipRules.ResetToCatalog((ShowReferenceItem)item, catalogLater);
        Assert.Equal([S(1), S(5), S(6)], Skipped(reset));
    }

    [Fact]
    public void SVC_027_filling_a_placeholder_copies_the_marks_and_keeps_the_items_identity()
    {
        var catalog = Verses(8, marked: [5, 6]);
        var placeholder = new PlaceholderItem
        {
            Id = I<ServiceItem>(300),
            Placeholder = new PlaceholderSpec { Label = "Opening song", KindFilter = ShowKind.Song },
            Actions = [new TriggeredAction { Trigger = new ShowEnded(), Commands = [new StopAudio()] }],
            TemplateOverrideId = I<Template>(4),
        };

        var filled = ShowItems.FillPlaceholder(placeholder, catalog);

        Assert.Equal(placeholder.Id, filled.Id);
        Assert.Equal(catalog.Id, filled.ShowId);
        Assert.Equal(placeholder.Placeholder, filled.FilledFrom);
        Assert.Equal(placeholder.Actions, filled.Actions);
        Assert.Equal(placeholder.TemplateOverrideId, filled.TemplateOverrideId);
        Assert.Equal([S(5), S(6)], Skipped(filled));
    }

    [Fact]
    public void Adding_a_show_with_no_marks_starts_with_nothing_skipped_and_a_new_item_id()
    {
        var a = ShowItems.AddReference(Verses(3));
        var b = ShowItems.AddReference(Verses(3));

        Assert.Empty(a.SkippedSlideIds);
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void Forking_carries_the_items_own_skip_list_not_the_catalog_marks()
    {
        var catalog = Verses(8, marked: [5, 6]);
        var item = (ShowReferenceItem)Apply(ShowItems.AddReference(catalog), catalog, 5, false);

        var copy = ShowCopying.Fork(item, catalog);

        Assert.Equal([S(6)], Skipped(copy));
    }

    [Fact]
    public void SHOW_008_duplicating_a_show_keeps_its_skipped_marks()
    {
        var copy = ShowCopying.Duplicate(Verses(8, marked: [5, 6]));

        Assert.Equal([5, 6], Enumerable.Range(1, 8).Where(n => copy.Slides[n - 1].SkippedByDefault));
    }

    // ---- SVC-021 ----------------------------------------------------------------------------

    [Fact]
    public void SVC_021_skipping_is_idempotent_and_unskipping_a_slide_that_is_not_skipped_changes_nothing()
    {
        var show = Verses(4);
        var item = ShowItems.AddReference(show);

        var once = SkipRules.SetSkipped(item, show, S(2), true);
        var twice = SkipRules.SetSkipped(once.Value, show, S(2), true);
        var noop = SkipRules.SetSkipped(item, show, S(3), false);

        Assert.Equal([S(2)], Skipped(once.Value));
        Assert.Same(once.Value, twice.Value);
        Assert.Same(item, noop.Value);
        Assert.True(twice.Succeeded && noop.Succeeded);
    }

    [Fact]
    public void Skipping_works_on_service_local_copies_too_and_keeps_the_item_type()
    {
        var show = Verses(4);
        var copy = ShowCopying.Fork(ShowItems.AddReference(show), show);

        var result = SkipRules.SetSkipped(copy, show, S(2), true);

        Assert.IsType<ShowCopyItem>(result.Value);
        Assert.Equal([S(2)], Skipped(result.Value));
    }

    [Fact]
    public void Skipping_a_slide_that_is_not_in_the_show_is_refused()
    {
        var show = Verses(2);
        var item = ShowItems.AddReference(show);

        var result = SkipRules.SetSkipped(item, show, S(99), true);

        Assert.False(result.Succeeded);
        Assert.Equal(SkipRules.UnknownSlideReason, result.RefusedReason);
        Assert.Same(item, result.Value);
    }

    // ---- SVC-025 / SHOW-008: cued audio ------------------------------------------------------

    [Fact]
    public void SVC_025_can_skip_is_false_only_when_the_audio_has_cues()
    {
        Assert.True(SkipRules.CanSkip(Verses(4)));
        Assert.True(SkipRules.CanSkip(Verses(4) with { Audio = new AudioTrack { MediaPath = LibraryRelativePath.Create("a.mp3") } }));
        Assert.False(SkipRules.CanSkip(CuedSong()));
    }

    [Fact]
    public void SVC_025_skipping_a_slide_of_a_cued_show_is_refused_with_a_reason_but_unskipping_is_allowed()
    {
        var show = CuedSong();
        var item = ShowItems.AddReference(show);

        var refused = SkipRules.SetSkipped(item, show, S(3), true);
        Assert.False(refused.Succeeded);
        Assert.Equal(SkipRules.CuedAudioReason, refused.RefusedReason);
        Assert.Same(item, refused.Value);

        var staleSkip = item with { SkippedSlideIds = [S(3)] }; // e.g. cues were added after skipping
        var allowed = SkipRules.SetSkipped(staleSkip, show, S(3), false);
        Assert.True(allowed.Succeeded);
        Assert.Empty(allowed.Value.SkippedSlideIds);
    }

    [Fact]
    public void SHOW_008_marking_a_slide_skipped_in_the_catalog_follows_the_same_cue_rule()
    {
        var cued = CuedSong();
        var refused = SkipRules.SetSkippedByDefault(cued, S(3), true);
        Assert.False(refused.Succeeded);
        Assert.Same(cued, refused.Value);

        var plain = Verses(4);
        var marked = SkipRules.SetSkippedByDefault(plain, S(3), true);
        Assert.True(marked.Succeeded);
        Assert.True(marked.Value.Slides[2].SkippedByDefault);
        Assert.False(plain.Slides[2].SkippedByDefault); // the original is unchanged

        var unmarked = SkipRules.SetSkippedByDefault(marked.Value, S(3), false);
        Assert.False(unmarked.Value.Slides[2].SkippedByDefault);
    }

    [Fact]
    public void SHOW_008_catalog_marks_on_a_slide_that_does_not_exist_are_refused()
    {
        var result = SkipRules.SetSkippedByDefault(Verses(2), S(9), true);
        Assert.Equal(SkipRules.UnknownSlideReason, result.RefusedReason);
    }

    [Fact]
    public void Catalog_marks_are_ignored_when_adding_a_show_that_has_cued_audio()
    {
        var cuedWithStaleMark = CuedSong() with
        {
            Slides = [.. CuedSong().Slides.Select((s, i) => i == 2 ? s with { SkippedByDefault = true } : s)],
        };

        Assert.Empty(SkipRules.DefaultsFor(cuedWithStaleMark));
        Assert.Empty(ShowItems.AddReference(cuedWithStaleMark).SkippedSlideIds);
    }

    // ---- SVC-022 policy ----------------------------------------------------------------------

    [Fact]
    public void SVC_022_presentations_fork_automatically_and_songs_prompt()
    {
        Assert.Equal(SlideEditAction.ForkAutomatically, EditPolicy.ForSlideEdit(SampleData.SmallPresentation()));
        Assert.Equal(SlideEditAction.PromptCatalogOrCopy, EditPolicy.ForSlideEdit(SampleData.Song()));
    }
}
