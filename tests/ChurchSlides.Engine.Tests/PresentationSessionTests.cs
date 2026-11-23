using ChurchSlides.Domain;
using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Navigation;
using ChurchSlides.Engine;
using static ChurchSlides.Engine.CommandOrigin;
using Kind = ChurchSlides.Engine.PresentationEventKind;

namespace ChurchSlides.Engine.Tests;

/// <summary>
/// The session as the operator and Actions drive it. Item IDs are 100+n and logged as n, slide IDs are unique across the
/// Service. The log merges states ("= item:slide") and events ("Kind item:slide") in the order they were raised.
/// </summary>
public class PresentationSessionTests
{
    private static Id<T> I<T>(int n) => new(new Guid(n, 0, 0, new byte[8]));

    private static int N<T>(Id<T> id) => BitConverter.ToInt32(id.Value.ToByteArray(), 0);

    private static Id<ServiceItem> It(int n) => I<ServiceItem>(100 + n);

    private static Id<Slide> S(int n) => I<Slide>(n);

    private static SlideRef R(int item, int slide) => new(It(item), S(slide));

    private static Show ShowWith(int id, params int[] slides) => new()
    {
        Id = I<Show>(10 + id),
        Title = $"Show {id}",
        Kind = ShowKind.Song,
        CategoryId = I<Category>(1),
        CopyrightPlacement = CopyrightPlacement.None,
        Slides = [.. slides.Select(n => new Slide { Id = S(n), Label = $"S{n}" })],
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
        Placeholder = new PlaceholderSpec { Label = "Song" },
    };

    private static readonly Show SongA = ShowWith(1, 1, 2, 3, 4);
    private static readonly Show SongB = ShowWith(2, 5, 6);
    private static readonly Show SongC = ShowWith(3, 7);
    private static readonly Show[] Catalog = [SongA, SongB, SongC];

    // 1: A (1, 2, 3 skipped, 4) | 2: placeholder | 3: B (5, 6) | 4: C (7 skipped) | 5: copy (8, 9)
    // Displayable order: 1:1 1:2 1:4 3:5 3:6 5:8 5:9
    private static Service Standard() => Of(Ref(1, SongA, 3), Placeholder(2), Ref(3, SongB), Ref(4, SongC, 7), Copy(5, ShowWith(4, 8, 9)));

    private static Service Of(params ServiceItem[] items) => new()
    {
        Id = I<Service>(1),
        Name = "Sunday",
        Date = new DateOnly(2026, 12, 6),
        Items = [.. items],
    };

    private sealed class Rig
    {
        public readonly List<string> Log = [];
        public readonly List<PresentationEvent> Events = [];
        public readonly List<Exception> Errors = [];
        public readonly PresentationSession Session;

        public Rig(Service service)
        {
            var shows = Catalog.ToDictionary(s => s.Id);
            Session = new PresentationSession(service, id => shows.GetValueOrDefault(id), Errors.Add);
            Session.StateChanged += s => Log.Add($"= {Fmt(s.Current)}");
            Session.EventRaised += e =>
            {
                Events.Add(e);
                var origin = e.Kind == Kind.ItemChanged ? $" {e.Origin}" : "";
                var where = e.ItemId is { } item ? $" {N(item) - 100}" + (e.SlideId is { } slide ? $":{N(slide)}" : "") : "";
                Log.Add($"{e.Kind}{where}{origin}");
            };
        }

        public PresentationState State => Session.State;

        /// <summary>Takes and clears the log.</summary>
        public string[] Take()
        {
            var taken = Log.ToArray();
            Log.Clear();
            return taken;
        }

        public Rig Live(int? item = null, int? slide = null)
        {
            if (item is { } i) Session.Select(It(i), slide is { } s ? S(s) : null, Operator);
            Session.StartPresenting(ServiceMode.Live, Operator);
            Log.Clear();
            Events.Clear();
            return this;
        }

        private static string Fmt(SlideRef? r) => r is { } x ? $"{N(x.ItemId) - 100}:{N(x.SlideId)}" : "none";
    }

    private static Rig Presenting(int? item = null, int? slide = null) => new Rig(Standard()).Live(item, slide);

    // ---- AS-02 / LIVE-005 / LIVE-007: slide moves ------------------------------------------------

    [Fact]
    public void AS_02_next_visits_exactly_the_displayable_slides_in_order_then_does_nothing()
    {
        var rig = Presenting();
        var visited = new List<SlideRef> { rig.State.Current!.Value };

        for (var n = 0; n < 20; n++)
        {
            rig.Session.Next(Operator);
            if (rig.State.Current!.Value != visited[^1]) visited.Add(rig.State.Current.Value);
        }

        Assert.Equal([R(1, 1), R(1, 2), R(1, 4), R(3, 5), R(3, 6), R(5, 8), R(5, 9)], visited);
        rig.Take();
        rig.Session.Next(Operator);
        Assert.Empty(rig.Take());
    }

    [Fact]
    public void LIVE_005_previous_from_an_items_first_slide_lands_on_the_previous_items_last_displayable_slide()
    {
        var rig = Presenting(3, 5);

        rig.Session.Previous(Operator);

        Assert.Equal(R(1, 4), rig.State.Current);
    }

    [Fact]
    public void LIVE_007_previous_at_the_start_does_nothing()
    {
        var rig = Presenting();

        rig.Session.Previous(Operator);

        Assert.Empty(rig.Take());
        Assert.Equal(R(1, 1), rig.State.Current);
    }

    // ---- LIVE-016: item moves --------------------------------------------------------------------

    [Fact]
    public void LIVE_016_next_and_previous_item_go_to_first_displayable_slides_past_items_with_none()
    {
        var rig = Presenting(1, 2);

        rig.Session.NextItem(Operator);
        Assert.Equal(R(3, 5), rig.State.Current); // past the placeholder

        rig.Session.NextItem(Operator);
        Assert.Equal(R(5, 8), rig.State.Current); // past the all-skipped item

        rig.Session.Next(Operator);
        rig.Session.PreviousItem(Operator); // from mid-item: the previous item, not the top of this one
        Assert.Equal(R(3, 5), rig.State.Current);

        rig.Session.PreviousItem(Operator);
        Assert.Equal(R(1, 1), rig.State.Current);

        rig.Take();
        rig.Session.PreviousItem(Operator);
        Assert.Empty(rig.Take());
    }

    [Fact]
    public void LIVE_016_next_item_at_the_end_does_nothing()
    {
        var rig = Presenting(5, 9);

        rig.Session.NextItem(Operator);

        Assert.Empty(rig.Take());
    }

    [Fact]
    public void LIVE_016_item_moves_from_a_skipped_slide()
    {
        var rig = Presenting(1, 3);

        rig.Session.NextItem(Operator);
        Assert.Equal(R(3, 5), rig.State.Current);

        rig.Session.GoToSlide(new SlideTarget(S(3), It(1)), Operator);
        rig.Session.PreviousItem(Operator);
        Assert.Equal(R(1, 3), rig.State.Current); // first item: nothing before it
    }

    [Fact]
    public void LIVE_016_an_item_header_goes_to_its_first_displayable_slide_and_an_item_with_none_does_nothing()
    {
        var song = ShowWith(1, 1, 2, 3, 4);
        var rig = new Rig(Of(Ref(1, song, 1), Placeholder(2), Ref(3, SongB))).Live(3);

        rig.Session.GoToItem(It(1), Operator);
        Assert.Equal(R(1, 2), rig.State.Current);

        rig.Take();
        rig.Session.GoToItem(It(2), Operator);
        rig.Session.GoToItem(It(99), Operator);
        Assert.Empty(rig.Take());
    }

    // ---- LIVE-017: skipped slides ----------------------------------------------------------------

    [Fact]
    public void LIVE_017_clicking_a_skipped_slide_displays_it_and_next_and_previous_go_to_the_nearest_displayable()
    {
        var rig = Presenting();

        rig.Session.GoToSlide(new SlideTarget(S(3), It(1)), Operator);
        Assert.Equal(R(1, 3), rig.State.Current);
        rig.Session.Next(Operator);
        Assert.Equal(R(1, 4), rig.State.Current);

        rig.Session.GoToSlide(new SlideTarget(S(7), It(4)), Operator);
        Assert.Equal(R(4, 7), rig.State.Current);
        rig.Session.Previous(Operator);
        Assert.Equal(R(3, 6), rig.State.Current);
    }

    // ---- ACT-013: going to the current slide -----------------------------------------------------

    [Fact]
    public void ACT_013_going_to_the_current_slide_changes_nothing_and_fires_nothing()
    {
        var rig = Presenting(1, 2);
        var before = rig.State;

        rig.Session.GoToSlide(new SlideTarget(S(2), It(1)), Operator);
        rig.Session.GoToSlide(new SlideTarget(S(2)), Automation);

        Assert.Empty(rig.Take());
        Assert.Same(before, rig.State);
    }

    [Fact]
    public void ACT_013_going_to_the_current_item_from_a_later_slide_goes_to_its_first_slide_as_a_slide_change()
    {
        var rig = Presenting(1, 4);

        rig.Session.GoToItem(It(1), Operator);

        Assert.Equal(["SlideEnded 1:4", "= 1:1", "SlideDisplayed 1:1"], rig.Take());
    }

    // ---- ACT-005: event order --------------------------------------------------------------------

    [Fact]
    public void ACT_005_a_slide_change_within_an_item_ends_the_slide_then_displays_the_next()
    {
        var rig = Presenting();

        rig.Session.Next(Operator);

        Assert.Equal(["SlideEnded 1:1", "= 1:2", "SlideDisplayed 1:2"], rig.Take());
    }

    [Fact]
    public void ACT_005_an_item_change_by_the_operator_fires_the_full_order_with_the_origin()
    {
        var rig = Presenting(1, 4);

        rig.Session.Next(Operator);

        Assert.Equal(
            ["ItemChanged 3:5 Operator", "SlideEnded 1:4", "ShowEnded 1", "= 3:5", "ShowStarted 3", "SlideDisplayed 3:5"],
            rig.Take());
    }

    [Fact]
    public void ACT_005_DEC_020_an_item_change_by_automation_carries_the_automation_origin_on_every_event()
    {
        var rig = Presenting();

        rig.Session.GoToItem(It(5), Automation);

        Assert.Equal(
            ["ItemChanged 5:8 Automation", "SlideEnded 1:1", "ShowEnded 1", "= 5:8", "ShowStarted 5", "SlideDisplayed 5:8"],
            rig.Take());
        Assert.All(rig.Events, e => Assert.Equal(Automation, e.Origin));
    }

    [Fact]
    public void ACT_005_LIVE_009_go_live_fires_service_started_then_shows_the_slide_then_show_started_and_slide_displayed()
    {
        var rig = new Rig(Standard());

        rig.Session.StartPresenting(ServiceMode.Rehearse, Operator);

        Assert.Equal(["ServiceStarted", "= 1:1", "ShowStarted 1", "SlideDisplayed 1:1"], rig.Take());
    }

    [Fact]
    public void ACT_005_a_command_from_a_listener_runs_after_the_move_with_its_own_complete_sequence()
    {
        var rig = Presenting();
        var once = true;
        rig.Session.EventRaised += e =>
        {
            if (e.Kind == Kind.SlideDisplayed && once)
            {
                once = false;
                rig.Session.Next(Automation);
                rig.Log.Add("(Next called)");
            }
        };

        rig.Session.Next(Operator);

        Assert.Equal(
            [
                "SlideEnded 1:1", "= 1:2", "SlideDisplayed 1:2", "(Next called)",
                "SlideEnded 1:2", "= 1:4", "SlideDisplayed 1:4",
            ],
            rig.Take());
    }

    [Fact]
    public void ACT_005_a_command_from_a_state_listener_is_queued_too()
    {
        var rig = Presenting();
        var once = true;
        rig.Session.StateChanged += _ =>
        {
            if (once)
            {
                once = false;
                rig.Session.Next(Automation);
            }
        };

        rig.Session.Next(Operator);

        Assert.Equal(
            ["SlideEnded 1:1", "= 1:2", "SlideDisplayed 1:2", "SlideEnded 1:2", "= 1:4", "SlideDisplayed 1:4"],
            rig.Take());
    }

    // ---- SAFE-006: listener failures -------------------------------------------------------------

    [Fact]
    public void SAFE_006_a_throwing_listener_is_reported_and_the_move_still_completes()
    {
        var rig = new Rig(Standard());
        var boom = new InvalidOperationException("boom");
        rig.Session.EventRaised += _ => throw boom;
        rig.Session.StateChanged += _ => throw boom;
        rig.Live();
        rig.Errors.Clear();

        rig.Session.NextItem(Operator);

        Assert.Equal(
            ["ItemChanged 3:5 Operator", "SlideEnded 1:1", "ShowEnded 1", "= 3:5", "ShowStarted 3", "SlideDisplayed 3:5"],
            rig.Take());
        Assert.Equal(6, rig.Errors.Count);
        Assert.All(rig.Errors, e => Assert.Same(boom, e));
        Assert.Equal(R(3, 5), rig.State.Current);
    }

    // ---- DEC-021 / LIVE-009 / LIVE-018 / MODE-005: presenting or not ----------------------------

    [Fact]
    public void DEC_021_LIVE_018_not_presenting_click_selects_only_and_other_commands_do_nothing()
    {
        var rig = new Rig(Standard());

        rig.Session.Next(Operator);
        rig.Session.NextItem(Operator);
        rig.Session.GoToItem(It(3), Operator);
        rig.Session.GoToSlide(new SlideTarget(S(5), It(3)), Operator);
        rig.Session.ToggleScreen(ScreenState.Black, Operator);
        rig.Session.SetScreen(ScreenState.Logo, Automation);
        rig.Session.StopPresenting(Operator);
        Assert.Empty(rig.Take());

        rig.Session.Select(It(3), S(6), Operator);

        Assert.Equal(["= none"], rig.Take());
        Assert.False(rig.State.IsPresenting);
        Assert.Null(rig.State.Current);
        Assert.Null(rig.State.Mode);
        Assert.Equal(R(3, 6), rig.State.Selected);
        Assert.Empty(rig.Events);
    }

    [Fact]
    public void DEC_021_selecting_a_header_selects_its_first_displayable_slide_and_unknown_targets_do_nothing()
    {
        var rig = new Rig(Standard());

        rig.Session.Select(It(1), null, Operator);
        Assert.Equal(R(1, 1), rig.State.Selected);

        rig.Take();
        rig.Session.Select(It(1), null, Operator); // already selected
        rig.Session.Select(It(2), null, Operator); // nothing displayable
        rig.Session.Select(It(1), S(9), Operator); // slide of another item
        Assert.Empty(rig.Take());
        Assert.Equal(R(1, 1), rig.State.Selected);
    }

    [Fact]
    public void LIVE_009_MODE_005_go_live_starts_at_the_selection_even_if_skipped_with_the_mode_in_the_state()
    {
        var rig = new Rig(Standard());
        rig.Session.Select(It(4), S(7), Operator);
        rig.Take();

        rig.Session.StartPresenting(ServiceMode.Live, Operator);

        Assert.Equal(["ServiceStarted", "= 4:7", "ShowStarted 4", "SlideDisplayed 4:7"], rig.Take());
        Assert.True(rig.State.IsPresenting);
        Assert.Equal(ServiceMode.Live, rig.State.Mode);
        Assert.Null(rig.State.Selected);
        Assert.Equal(ScreenState.Normal, rig.State.Screen);

        rig.Session.StartPresenting(ServiceMode.Rehearse, Operator); // already presenting
        rig.Session.Select(It(1), S(1), Operator); // selection only applies while not presenting
        Assert.Empty(rig.Take());
    }

    [Fact]
    public void LIVE_009_a_service_with_nothing_displayable_starts_presenting_with_no_current_slide()
    {
        var rig = new Rig(Of(Placeholder(1)));

        rig.Session.StartPresenting(ServiceMode.Rehearse, Operator);

        Assert.Equal(["ServiceStarted", "= none"], rig.Take());
        Assert.True(rig.State.IsPresenting);

        rig.Session.Previous(Operator);
        rig.Session.Next(Operator);
        rig.Session.NextItem(Operator);
        Assert.Empty(rig.Take());
    }

    [Fact]
    public void LIVE_018_stop_presenting_selects_the_last_slide_fires_only_presenting_stopped_and_go_live_resumes()
    {
        var rig = Presenting(3, 6);
        rig.Session.ToggleScreen(ScreenState.Black, Operator);
        rig.Take();

        rig.Session.StopPresenting(Operator);

        Assert.Equal(["= none", "PresentingStopped"], rig.Take());
        Assert.False(rig.State.IsPresenting);
        Assert.Null(rig.State.Mode);
        Assert.Equal(R(3, 6), rig.State.Selected);
        Assert.Equal(ScreenState.Normal, rig.State.Screen);

        rig.Session.StartPresenting(ServiceMode.Live, Operator);
        Assert.Equal(R(3, 6), rig.State.Current);
    }

    // ---- LIVE-013 / LIVE-014 / ACT-026..028 / ACT-033: screen state ------------------------------

    [Fact]
    public void LIVE_013_toggles_are_exclusive_and_a_second_press_turns_them_off()
    {
        var rig = Presenting();

        rig.Session.ToggleScreen(ScreenState.Black, Operator);
        Assert.Equal(ScreenState.Black, rig.State.Screen);
        rig.Session.ToggleScreen(ScreenState.Clear, Operator);
        Assert.Equal(ScreenState.Clear, rig.State.Screen);
        rig.Session.ToggleScreen(ScreenState.Logo, Operator);
        Assert.Equal(ScreenState.Logo, rig.State.Screen);
        rig.Session.ToggleScreen(ScreenState.Logo, Operator);
        Assert.Equal(ScreenState.Normal, rig.State.Screen);

        Assert.Equal(["= 1:1", "= 1:1", "= 1:1", "= 1:1"], rig.Take()); // states only, no events
    }

    [Fact]
    public void ACT_026_ACT_027_ACT_028_ACT_033_set_screen_sets_without_toggling_and_normal_clears()
    {
        var rig = Presenting();

        rig.Session.SetScreen(ScreenState.Clear, Automation);
        rig.Session.SetScreen(ScreenState.Clear, Automation);
        Assert.Equal(ScreenState.Clear, rig.State.Screen);
        rig.Session.SetScreen(ScreenState.Black, Automation);
        Assert.Equal(ScreenState.Black, rig.State.Screen);
        rig.Session.SetScreen(ScreenState.Logo, Automation);
        Assert.Equal(ScreenState.Logo, rig.State.Screen);
        rig.Session.SetScreen(ScreenState.Normal, Automation);
        Assert.Equal(ScreenState.Normal, rig.State.Screen);

        Assert.Equal(4, rig.Take().Length); // the repeated Clear changed nothing
    }

    [Fact]
    public void LIVE_014_navigating_keeps_the_screen_state()
    {
        var rig = Presenting();
        rig.Session.ToggleScreen(ScreenState.Clear, Operator);

        rig.Session.Next(Operator);
        rig.Session.NextItem(Operator);

        Assert.Equal(R(3, 5), rig.State.Current);
        Assert.Equal(ScreenState.Clear, rig.State.Screen);
    }

    // ---- LIVE-008: what comes next ---------------------------------------------------------------

    [Fact]
    public void LIVE_008_next_slide_and_next_item_follow_skips_and_items_and_are_null_at_the_end()
    {
        var rig = Presenting(1, 2);
        Assert.Equal(R(1, 4), rig.State.NextSlide);
        Assert.Equal(It(3), rig.State.NextItemId);

        rig.Session.GoToSlide(new SlideTarget(S(6), It(3)), Operator);
        Assert.Equal(R(5, 8), rig.State.NextSlide);
        Assert.Equal(It(5), rig.State.NextItemId);

        rig.Session.Next(Operator);
        rig.Session.Next(Operator);
        Assert.Null(rig.State.NextSlide);
        Assert.Null(rig.State.NextItemId);

        Assert.Null(new Rig(Standard()).State.NextSlide); // not presenting
    }

    // ---- Position ("item 4 of 9", "slide 2 of 6") ------------------------------------------------

    [Fact]
    public void LIVE_016_position_counts_every_item_and_every_slide_of_the_item_skipped_ones_too()
    {
        var rig = Presenting(1, 4);

        Assert.Equal((1, 5, 4, 4), (rig.State.ItemNumber, rig.State.ItemCount, rig.State.SlideNumber, rig.State.SlideCount));

        rig.Session.NextItem(Operator);
        Assert.Equal((3, 5, 1, 2), (rig.State.ItemNumber, rig.State.ItemCount, rig.State.SlideNumber, rig.State.SlideCount));

        var idle = new Rig(Standard()).State;
        Assert.Null(idle.ItemNumber);
        Assert.Null(idle.SlideNumber);
        Assert.Null(idle.SlideCount);
    }

    // ---- LIVE-040 / LIVE-041: the Service changes underneath -------------------------------------

    [Fact]
    public void LIVE_040_refresh_with_an_edited_current_slide_gives_a_new_state_and_no_events()
    {
        var rig = Presenting(3, 6);
        var before = rig.State;
        var edited = Standard() with { Items = [Ref(6, SongC), .. Standard().Items] };

        rig.Session.Refresh(edited);

        Assert.Equal(["= 3:6"], rig.Take());
        Assert.NotSame(before, rig.State);
        Assert.Same(edited, rig.State.Service);
        Assert.False(rig.State.CurrentRemoved);
        Assert.Equal(R(3, 6), rig.State.Current);
        Assert.Equal(4, rig.State.ItemNumber); // re-anchored after an item was inserted above
        Assert.Equal(6, rig.State.ItemCount);
    }

    [Fact]
    public void LIVE_041_a_removed_current_slide_is_kept_and_next_and_previous_go_to_its_old_neighbours()
    {
        var rig = Presenting(1, 2);
        var songWithoutSlide2 = SongA with { Slides = [.. SongA.Slides.Where(s => s.Id != S(2))] };
        var withoutSlide2 = Standard() with { Items = [Copy(1, songWithoutSlide2, 3), .. Standard().Items[1..]] };

        rig.Session.Refresh(withoutSlide2);

        Assert.True(rig.State.CurrentRemoved);
        Assert.Equal(R(1, 2), rig.State.Current);
        Assert.Null(rig.State.ItemNumber);
        Assert.Null(rig.State.SlideNumber);
        Assert.Equal(R(1, 4), rig.State.NextSlide); // 3 survived but is skipped: the nearest displayable after it

        rig.Session.Previous(Operator);
        Assert.Equal(R(1, 1), rig.State.Current);
        Assert.False(rig.State.CurrentRemoved);

        rig.Session.Refresh(Standard());
        rig.Session.GoToSlide(new SlideTarget(S(2), It(1)), Operator);
        rig.Session.Refresh(withoutSlide2);
        rig.Take();
        rig.Session.Next(Operator);
        Assert.Equal(["SlideEnded 1:2", "= 1:4", "SlideDisplayed 1:4"], rig.Take());
    }

    [Fact]
    public void LIVE_041_a_removed_current_item_is_kept_and_moves_go_to_its_old_neighbours()
    {
        var withoutItem3 = Of(Ref(1, SongA, 3), Placeholder(2), Ref(4, SongC, 7), Copy(5, ShowWith(4, 8, 9)));

        var rig = Presenting(3, 5);
        rig.Session.Refresh(withoutItem3);
        Assert.True(rig.State.CurrentRemoved);
        rig.Take();
        rig.Session.Next(Operator);
        Assert.Equal(
            ["ItemChanged 5:8 Operator", "SlideEnded 3:5", "ShowEnded 3", "= 5:8", "ShowStarted 5", "SlideDisplayed 5:8"],
            rig.Take());

        rig = Presenting(3, 6);
        rig.Session.Refresh(withoutItem3);
        rig.Session.Previous(Operator);
        Assert.Equal(R(1, 4), rig.State.Current);

        rig = Presenting(3, 5);
        rig.Session.Refresh(withoutItem3);
        rig.Session.NextItem(Operator);
        Assert.Equal(R(5, 8), rig.State.Current);

        rig = Presenting(3, 5);
        rig.Session.Refresh(withoutItem3);
        rig.Session.PreviousItem(Operator);
        Assert.Equal(R(1, 1), rig.State.Current);
    }

    [Fact]
    public void LIVE_041_with_everything_after_it_removed_next_does_nothing()
    {
        var rig = Presenting(3, 6);

        rig.Session.Refresh(Of(Ref(1, SongA, 3), Placeholder(2)));
        rig.Take();
        rig.Session.Next(Operator);
        rig.Session.NextItem(Operator);

        Assert.Empty(rig.Take());
        Assert.Equal(R(3, 6), rig.State.Current);
        Assert.Null(rig.State.NextSlide);
    }

    [Fact]
    public void LIVE_041_a_removed_slide_that_comes_back_is_current_again()
    {
        var rig = Presenting(3, 5);

        rig.Session.Refresh(Of(Ref(1, SongA)));
        rig.Session.Refresh(Standard());

        Assert.False(rig.State.CurrentRemoved);
        Assert.Equal(3, rig.State.ItemNumber);
    }

    [Fact]
    public void LIVE_041_a_removed_selection_is_cleared_on_refresh()
    {
        var rig = new Rig(Standard());
        rig.Session.Select(It(3), S(5), Operator);

        rig.Session.Refresh(Of(Ref(1, SongA)));

        Assert.Null(rig.State.Selected);
    }

    // ---- ACT-022 / ACT-041: GoToSlide targets ----------------------------------------------------

    [Fact]
    public void ACT_022_go_to_slide_without_an_item_looks_in_the_current_item()
    {
        var rig = Presenting(3, 5);

        rig.Session.GoToSlide(new SlideTarget(S(6)), Automation);
        Assert.Equal(R(3, 6), rig.State.Current);

        rig.Take();
        rig.Session.GoToSlide(new SlideTarget(S(1)), Automation); // in another item
        Assert.Empty(rig.Take());
    }

    [Fact]
    public void ACT_041_go_to_an_unknown_item_or_slide_does_nothing()
    {
        var rig = Presenting();

        rig.Session.GoToSlide(new SlideTarget(S(5), It(99)), Automation);
        rig.Session.GoToSlide(new SlideTarget(S(99), It(3)), Automation);
        rig.Session.GoToItem(It(99), Automation);

        Assert.Empty(rig.Take());
    }
}
