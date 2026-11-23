using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Navigation;
using static ChurchSlides.Domain.Tests.Fx;

namespace ChurchSlides.Domain.Tests;

public class ServiceSequenceTests
{
    // Item IDs are 100+, Show IDs 10+, slide IDs are unique across the whole Service.

    private static Id<ServiceItem> It(int n) => I<ServiceItem>(100 + n);

    private static SlideRef R(int item, int slide) => new(It(item), S(slide));

    /// <summary>A Show with ID <paramref name="id"/> and the given slide numbers.</summary>
    private static Show ShowWith(int id, params int[] slides) => ShowOf(slides.Select(n => SlideOf(n))) with { Id = I<Show>(10 + id) };

    private static Show Cued(Show show) => show with
    {
        Audio = new AudioTrack
        {
            MediaPath = LibraryRelativePath.Create("media/audio/song.mp3"),
            Cues = [new Cue(show.Slides[0].Id, TimeSpan.Zero)],
        },
    };

    private static ShowReferenceItem Ref(int item, Show show, params int[] skipped) => new()
    {
        Id = It(item),
        ShowId = show.Id,
        SkippedSlideIds = [.. skipped.Select(S)],
    };

    private static ShowCopyItem Copy(int item, Show show, params int[] skipped) => new()
    {
        Id = It(item),
        LocalShow = show,
        SkippedSlideIds = [.. skipped.Select(S)],
    };

    private static PlaceholderItem Placeholder(int item) => new()
    {
        Id = It(item),
        Placeholder = new PlaceholderSpec { Label = "Opening song" },
    };

    private static ScriptureItem Scripture(int item, Show? generated) => new()
    {
        Id = It(item),
        Scripture = new ScriptureSpec { BibleId = "esv", Reference = "Ps 23" },
        Generated = generated,
    };

    private static ServiceSequence Build(IEnumerable<Show> catalog, params ServiceItem[] items)
    {
        var shows = catalog.ToDictionary(s => s.Id);
        var service = ServiceWith() with { Items = [.. items] };
        return ServiceSequence.Build(service, id => shows.GetValueOrDefault(id));
    }

    // song (1..4, 2 and 3 skipped) | placeholder | missing | ungenerated scripture | all skipped (5, 6) | empty |
    // scripture (7, 8) | copy (9, 10)
    private static ServiceSequence Mixed()
    {
        var song = ShowWith(1, 1, 2, 3, 4);
        var allSkipped = ShowWith(2, 5, 6);
        var empty = ShowWith(3);
        return Build(
            [song, allSkipped, empty],
            Ref(1, song, 2, 3),
            Placeholder(2),
            Ref(3, ShowWith(99, 50)),
            Scripture(4, null),
            Ref(5, allSkipped, 5, 6),
            Ref(6, empty),
            Scripture(7, ShowWith(4, 7, 8)),
            Copy(8, ShowWith(5, 9, 10)));
    }

    // ---- LIVE-006 / SVC-034 / SVC-026: what is listed and what is displayable -----------------

    [Fact]
    public void LIVE_006_SVC_034_SVC_026_every_item_and_slide_is_listed_and_only_the_right_ones_are_displayable()
    {
        var seq = Mixed();

        Assert.Equal(
            [
                SequenceItemStatus.Show, SequenceItemStatus.UnfilledPlaceholder, SequenceItemStatus.MissingShow,
                SequenceItemStatus.NotGenerated, SequenceItemStatus.Show, SequenceItemStatus.Show,
                SequenceItemStatus.Show, SequenceItemStatus.Show,
            ],
            seq.Items.Select(i => i.Status));
        Assert.Equal(Enumerable.Range(1, 8).Select(It), seq.Items.Select(i => i.ItemId));
        Assert.Equal([true, false, false, false, false, false, true, true], seq.Items.Select(i => i.HasDisplayable));

        var song = seq.Items[0];
        Assert.Equal([S(1), S(2), S(3), S(4)], song.Slides.Select(s => s.SlideId));
        Assert.Equal([false, true, true, false], song.Slides.Select(s => s.IsSkipped));
        Assert.Equal([true, false, false, true], song.Slides.Select(s => s.IsDisplayable));
        Assert.Equal(I<Show>(11), song.Show?.Id);

        Assert.All(seq.Items.Skip(1).Take(3), i => Assert.Empty(i.Slides));
        Assert.All(seq.Items.Skip(1).Take(3), i => Assert.Null(i.Show));
        Assert.Equal([true, true], seq.Items[4].Slides.Select(s => s.IsSkipped));
        Assert.Empty(seq.Items[5].Slides);
        Assert.Equal(I<Show>(14), seq.Items[6].Show?.Id); // the generated Scripture Show

        Assert.Equal(
            [R(1, 1), R(1, 2), R(1, 3), R(1, 4), R(5, 5), R(5, 6), R(7, 7), R(7, 8), R(8, 9), R(8, 10)],
            seq.All.AsEnumerable());
        Assert.True(seq.Contains(R(1, 2)));
        Assert.False(seq.Contains(R(5, 1)));
        Assert.Equal(new SequenceSlide(S(2), IsSkipped: true), seq.Find(R(1, 2)));
        Assert.Null(seq.Find(R(2, 1)));
    }

    [Fact]
    public void SVC_025_a_cued_show_ignores_a_stale_skip_list()
    {
        var cued = Cued(ShowWith(1, 1, 2, 3));

        var seq = Build([cued], Ref(1, cued, 2, 3), Copy(2, Cued(ShowWith(2, 4, 5)), 5));

        Assert.All(seq.Items.SelectMany(i => i.Slides), s => Assert.False(s.IsSkipped));
        Assert.All(seq.Items.SelectMany(i => i.Slides), s => Assert.True(s.IsDisplayable));
    }

    [Fact]
    public void SVC_021_a_show_copys_skip_list_is_honored()
    {
        var seq = Build([], Copy(1, ShowWith(1, 1, 2, 3), 2));

        Assert.Equal([true, false, true], seq.Items[0].Slides.Select(s => s.IsDisplayable));
        Assert.Equal(R(1, 3), seq.NextDisplayable(R(1, 1)));
    }

    [Fact]
    public void SVC_021_scripture_slides_are_never_skipped()
    {
        Assert.All(Mixed().Items[6].Slides, s => Assert.True(s.IsDisplayable));
    }

    // ---- LIVE-005 / LIVE-007: slide moves ---------------------------------------------------------

    [Fact]
    public void LIVE_005_next_and_previous_cross_item_boundaries_past_items_with_nothing_displayable()
    {
        var seq = Mixed();

        Assert.Equal(R(7, 7), seq.NextDisplayable(R(1, 4)));
        Assert.Equal(R(8, 9), seq.NextDisplayable(R(7, 8)));
        Assert.Equal(R(1, 4), seq.PreviousDisplayable(R(7, 7)));
        Assert.Equal(R(7, 8), seq.PreviousDisplayable(R(8, 9)));
        Assert.Equal(R(1, 4), seq.NextDisplayable(R(1, 1))); // past the skipped 2 and 3
    }

    [Fact]
    public void LIVE_007_next_and_previous_do_nothing_at_the_ends()
    {
        var seq = Mixed();

        Assert.Null(seq.NextDisplayable(R(8, 10)));
        Assert.Null(seq.PreviousDisplayable(R(1, 1)));
        Assert.Equal(R(1, 1), seq.FirstDisplayable());
    }

    [Fact]
    public void LIVE_007_unknown_slides_and_a_service_with_nothing_displayable_give_null()
    {
        var seq = Mixed();
        Assert.Null(seq.NextDisplayable(R(2, 1)));
        Assert.Null(seq.PreviousDisplayable(R(1, 99)));

        var nothing = Build([], Placeholder(1), Scripture(2, null));
        Assert.Null(nothing.FirstDisplayable());
        Assert.Empty(nothing.All);
    }

    // ---- LIVE-017: from a skipped slide -----------------------------------------------------------

    [Fact]
    public void LIVE_017_next_and_previous_from_a_skipped_slide_go_to_the_nearest_displayable()
    {
        var seq = Mixed();

        Assert.Equal(R(1, 4), seq.NextDisplayable(R(1, 2)));
        Assert.Equal(R(1, 1), seq.PreviousDisplayable(R(1, 3)));
        Assert.Equal(R(7, 7), seq.NextDisplayable(R(5, 5))); // in an item where every slide is skipped
        Assert.Equal(R(1, 4), seq.PreviousDisplayable(R(5, 6)));
    }

    // ---- LIVE-016: item moves ---------------------------------------------------------------------

    [Fact]
    public void LIVE_016_next_and_previous_item_skip_items_with_nothing_displayable_and_stop_at_the_ends()
    {
        var seq = Mixed();

        Assert.Equal(R(7, 7), seq.NextItemWithDisplayable(It(1)));
        Assert.Equal(R(8, 9), seq.NextItemWithDisplayable(It(7)));
        Assert.Null(seq.NextItemWithDisplayable(It(8)));

        Assert.Equal(R(7, 7), seq.PreviousItemWithDisplayable(It(8)));
        Assert.Equal(R(1, 1), seq.PreviousItemWithDisplayable(It(7)));
        Assert.Null(seq.PreviousItemWithDisplayable(It(1)));

        // From an item with nothing displayable.
        Assert.Equal(R(7, 7), seq.NextItemWithDisplayable(It(3)));
        Assert.Equal(R(1, 1), seq.PreviousItemWithDisplayable(It(5)));
        Assert.Null(seq.NextItemWithDisplayable(I<ServiceItem>(999)));
    }

    [Fact]
    public void LIVE_016_first_displayable_of_an_item_skips_its_skipped_slides_and_is_null_when_it_has_none()
    {
        var song = ShowWith(1, 1, 2, 3);
        var seq = Build([song], Ref(1, song, 1));

        Assert.Equal(R(1, 2), seq.FirstDisplayableOf(It(1)));
        Assert.Null(Mixed().FirstDisplayableOf(It(5)));
        Assert.Null(Mixed().FirstDisplayableOf(It(2)));
        Assert.Equal(R(1, 2), seq.FirstDisplayable());
    }

    [Fact]
    public void LIVE_016_the_same_show_twice_gives_two_distinct_positions()
    {
        var song = ShowWith(1, 1, 2);
        var seq = Build([song], Ref(1, song), Ref(2, song));

        Assert.Equal(R(2, 1), seq.NextDisplayable(R(1, 2)));
        Assert.Equal(R(1, 2), seq.PreviousDisplayable(R(2, 1)));
    }
}
