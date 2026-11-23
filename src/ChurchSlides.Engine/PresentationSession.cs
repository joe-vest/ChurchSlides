using ChurchSlides.Domain;
using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Navigation;
using Kind = ChurchSlides.Engine.PresentationEventKind;

namespace ChurchSlides.Engine;

/// <summary>
/// The one <see cref="ICommandApi"/> implementation, for one open Service (ARCH 6.1, 6.2). Not thread-safe: call it
/// from one thread (the UI thread). Events are raised synchronously on that thread in the fixed ACT-005 order:
/// ItemChanged (item change only) → SlideEnded → ShowEnded (item change only) → <see cref="StateChanged"/> →
/// ShowStarted (item change only) → SlideDisplayed. A command issued by a listener is queued and runs after the
/// current command finishes (ARCH 6.3). A throwing listener goes to <c>onListenerError</c> and the move goes on (SAFE-006).
/// </summary>
public sealed class PresentationSession : ICommandApi
{
    private readonly Func<Id<Show>, Show?> _findShow;
    private readonly Action<Exception> _onListenerError;
    private readonly Queue<Action> _queue = new();
    private bool _running;

    // The last sequence that held the current slide: its order gives a removed slide's old neighbours (LIVE-041).
    private ServiceSequence _anchor;

    /// <param name="findShow">Looks up catalog Shows; null means missing (SVC-026).</param>
    /// <param name="onListenerError">Receives exceptions thrown by listeners (the App logs them).</param>
    public PresentationSession(Service service, Func<Id<Show>, Show?> findShow, Action<Exception> onListenerError)
    {
        _findShow = findShow;
        _onListenerError = onListenerError;
        _anchor = ServiceSequence.Build(service, findShow);
        State = new PresentationState { Service = service, Sequence = _anchor };
    }

    public PresentationState State { get; private set; }

    /// <summary>Every new state (ACT-005 step 4). The Output, Preview and lists render from it.</summary>
    public event Action<PresentationState>? StateChanged;

    /// <summary>Every step of a move. Audio and Actions hook in here.</summary>
    public event Action<PresentationEvent>? EventRaised;

    private ServiceSequence Sequence => State.Sequence;

    /// <summary>
    /// Rebuilds the sequence after the Service or a Show changed and re-anchors by (item, slide). A current slide that
    /// is still there stays, with no events (LIVE-040). One that is gone stays current, marked removed (LIVE-041).
    /// A removed selection is cleared.
    /// </summary>
    public void Refresh(Service service) => Run(() =>
    {
        var sequence = ServiceSequence.Build(service, _findShow);
        SetState(State with
        {
            Service = service,
            Sequence = sequence,
            Selected = State.Selected is { } s && sequence.Contains(s) ? s : null,
            CurrentRemoved = State.Current is { } c && !sequence.Contains(c),
        });
    });

    public void Next(CommandOrigin origin) => Run(() => MoveTo(State.NextSlide, origin));

    public void Previous(CommandOrigin origin) => Run(() => MoveTo(
        State.Current is { } c ? Step(Sequence, c, -1) : null,
        origin));

    public void NextItem(CommandOrigin origin) => Run(() => MoveTo(
        State.NextItemId is { } item ? Sequence.FirstDisplayableOf(item) : null,
        origin));

    public void PreviousItem(CommandOrigin origin) => Run(() => MoveTo(
        State.Current is { } c ? ItemStep(Sequence, c.ItemId, -1) : null,
        origin));

    public void GoToSlide(SlideTarget target, CommandOrigin origin) => Run(() =>
    {
        if ((target.ItemId ?? State.Current?.ItemId) is not { } item) return;
        var slide = new SlideRef(item, target.SlideId);
        if (Sequence.Contains(slide)) MoveTo(slide, origin);
    });

    public void GoToItem(Id<ServiceItem> itemId, CommandOrigin origin) =>
        Run(() => MoveTo(Sequence.FirstDisplayableOf(itemId), origin));

    public void ToggleScreen(ScreenState screen, CommandOrigin origin) =>
        Run(() => Screen(State.Screen == screen ? ScreenState.Normal : screen));

    public void SetScreen(ScreenState screen, CommandOrigin origin) => Run(() => Screen(screen));

    public void Select(Id<ServiceItem> itemId, Id<Slide>? slideId, CommandOrigin origin) => Run(() =>
    {
        if (State.IsPresenting) return;
        var slide = slideId is { } id ? new SlideRef(itemId, id) : Sequence.FirstDisplayableOf(itemId);
        if (slide is { } s && Sequence.Contains(s) && s != State.Selected) SetState(State with { Selected = s });
    });

    public void StartPresenting(ServiceMode mode, CommandOrigin origin) => Run(() =>
    {
        if (State.IsPresenting) return;
        var start = State.Selected ?? Sequence.FirstDisplayable();

        Raise(Kind.ServiceStarted, null, null, origin);
        SetState(State with { IsPresenting = true, Mode = mode, Selected = null, Current = start, Screen = ScreenState.Normal });
        if (start is { } s) Entered(s, itemChanged: true, origin);
    });

    public void StopPresenting(CommandOrigin origin) => Run(() =>
    {
        if (!State.IsPresenting) return;
        SetState(State with
        {
            IsPresenting = false,
            Mode = null,
            Selected = State.CurrentRemoved ? null : State.Current,
            Current = null,
            CurrentRemoved = false,
            Screen = ScreenState.Normal,
        });
        Raise(Kind.PresentingStopped, null, null, origin);
    });

    private void Run(Action command)
    {
        _queue.Enqueue(command);
        if (_running) return; // queued behind the command that is running (ARCH 6.3)

        _running = true;
        try
        {
            while (_queue.TryDequeue(out var next)) next();
        }
        finally
        {
            _running = false;
            _queue.Clear();
        }
    }

    private void MoveTo(SlideRef? target, CommandOrigin origin)
    {
        if (!State.IsPresenting || target is not { } to || to == State.Current) return;

        var from = State.Current;
        var itemChanged = from?.ItemId != to.ItemId;
        if (from is { } f)
        {
            if (itemChanged) Raise(Kind.ItemChanged, to.ItemId, to.SlideId, origin);
            Raise(Kind.SlideEnded, f.ItemId, f.SlideId, origin);
            if (itemChanged) Raise(Kind.ShowEnded, f.ItemId, null, origin);
        }

        SetState(State with { Current = to, CurrentRemoved = false });
        Entered(to, itemChanged, origin);
    }

    private void Entered(SlideRef slide, bool itemChanged, CommandOrigin origin)
    {
        if (itemChanged) Raise(Kind.ShowStarted, slide.ItemId, null, origin);
        Raise(Kind.SlideDisplayed, slide.ItemId, slide.SlideId, origin);
    }

    private void Screen(ScreenState screen)
    {
        if (State.IsPresenting && State.Screen != screen) SetState(State with { Screen = screen });
    }

    /// <summary>Stores the state with its look-ahead filled in, then raises <see cref="StateChanged"/>.</summary>
    private void SetState(PresentationState state)
    {
        if (!state.CurrentRemoved) _anchor = state.Sequence;
        var seq = state.Sequence;
        var (next, nextItem) = !state.IsPresenting ? (null, null)
            : state.Current is { } c ? (Step(seq, c, +1), ItemStep(seq, c.ItemId, +1))
            : (seq.FirstDisplayable(), seq.FirstDisplayable());
        State = state with { NextSlide = next, NextItemId = nextItem?.ItemId };
        Notify(StateChanged, State);
    }

    /// <summary>
    /// The nearest displayable slide in a direction. From a removed slide: the first slide that followed it in the
    /// old order and is still there, or the nearest displayable beyond that (LIVE-041).
    /// </summary>
    private SlideRef? Step(ServiceSequence seq, SlideRef from, int step)
    {
        SlideRef? Beyond(SlideRef s) => step > 0 ? seq.NextDisplayable(s) : seq.PreviousDisplayable(s);

        if (seq.Contains(from)) return Beyond(from);

        var old = _anchor.All;
        var at = old.IndexOf(from);
        for (var n = at + step; at >= 0 && n >= 0 && n < old.Length; n += step)
        {
            if (seq.Find(old[n]) is { } survivor) return survivor.IsDisplayable ? old[n] : Beyond(old[n]);
        }

        return null;
    }

    /// <summary>
    /// The first displayable slide of the nearest item in a direction that has one. From a removed item: the first
    /// item that followed it in the old order and is still there, or beyond that (LIVE-041).
    /// </summary>
    private SlideRef? ItemStep(ServiceSequence seq, Id<ServiceItem> from, int step)
    {
        SlideRef? Beyond(Id<ServiceItem> item) =>
            step > 0 ? seq.NextItemWithDisplayable(item) : seq.PreviousItemWithDisplayable(item);

        if (IndexOf(seq, from) >= 0) return Beyond(from);

        var at = IndexOf(_anchor, from);
        for (var n = at + step; at >= 0 && n >= 0 && n < _anchor.Items.Length; n += step)
        {
            var item = _anchor.Items[n].ItemId;
            if (IndexOf(seq, item) >= 0) return seq.FirstDisplayableOf(item) ?? Beyond(item);
        }

        return null;
    }

    private static int IndexOf(ServiceSequence seq, Id<ServiceItem> item)
    {
        for (var n = 0; n < seq.Items.Length; n++)
        {
            if (seq.Items[n].ItemId == item) return n;
        }

        return -1;
    }

    private void Raise(PresentationEventKind kind, Id<ServiceItem>? item, Id<Slide>? slide, CommandOrigin origin) =>
        Notify(EventRaised, new PresentationEvent(kind, item, slide, origin));

    private void Notify<T>(Action<T>? handlers, T value)
    {
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Action<T>>())
        {
            try
            {
                handler(value);
            }
            catch (Exception e)
            {
                _onListenerError(e);
            }
        }
    }
}
