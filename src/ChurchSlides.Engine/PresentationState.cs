using ChurchSlides.Domain;
using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Navigation;

namespace ChurchSlides.Engine;

/// <summary>
/// An immutable snapshot of the session (ARCH 6.2). The Output, Preview, operator list and remote render from it.
/// Not presenting: <see cref="Current"/> is null and the Output is black (LIVE-018).
/// </summary>
public sealed record PresentationState
{
    public required Service Service { get; init; }
    public required ServiceSequence Sequence { get; init; }
    public bool IsPresenting { get; init; }

    /// <summary>Set while presenting (MODE-005). The App saves it in the Service state file.</summary>
    public ServiceMode? Mode { get; init; }

    /// <summary>The selected slide while not presenting (DEC-021).</summary>
    public SlideRef? Selected { get; init; }

    /// <summary>The slide on the Output while presenting. Null if the Service has nothing displayable.</summary>
    public SlideRef? Current { get; init; }

    /// <summary>The current slide or its item was removed from the Service. The Output keeps the last frame (LIVE-041).</summary>
    public bool CurrentRemoved { get; init; }

    /// <summary>Where Next goes (LIVE-008). Actions are not predicted.</summary>
    public SlideRef? NextSlide { get; init; }

    /// <summary>Where Next item goes (LIVE-016).</summary>
    public Id<ServiceItem>? NextItemId { get; init; }

    public ScreenState Screen { get; init; }

    /// <summary>"Item 4 of 9": 1-based, counting every item. Null with no current slide or a removed one.</summary>
    public int? ItemNumber => Position?.Item + 1;

    public int ItemCount => Sequence.Items.Length;

    /// <summary>"Slide 2 of 6": 1-based, counting every slide of the item, Skipped ones too (LIVE-017).</summary>
    public int? SlideNumber => Position?.Slide + 1;

    public int? SlideCount => Position is { } p ? Sequence.Items[p.Item].Slides.Length : null;

    private (int Item, int Slide)? Position
    {
        get
        {
            if (Current is not { } c || CurrentRemoved) return null;
            for (var i = 0; i < Sequence.Items.Length; i++)
            {
                if (Sequence.Items[i].ItemId != c.ItemId) continue;
                for (var s = 0; s < Sequence.Items[i].Slides.Length; s++)
                {
                    if (Sequence.Items[i].Slides[s].SlideId == c.SlideId) return (i, s);
                }
            }

            return null;
        }
    }
}

/// <summary>What a <see cref="PresentationEvent"/> announces. Audio (C4.1) and Actions (C5.2) listen.</summary>
public enum PresentationEventKind
{
    ServiceStarted,

    /// <summary>First step of a move to a different item (ACT-005). Carries the new item and slide, and the origin (DEC-020).</summary>
    ItemChanged,

    SlideEnded,
    ShowEnded,
    ShowStarted,
    SlideDisplayed,

    /// <summary>The only event when presenting stops: no SlideEnded or ShowEnded.</summary>
    PresentingStopped,
}

/// <summary>
/// One step of a move. SlideEnded and ShowEnded carry the slide and item being left, ItemChanged, ShowStarted and
/// SlideDisplayed the ones entered. Show events have no slide; ServiceStarted and PresentingStopped have neither.
/// </summary>
public sealed record PresentationEvent(
    PresentationEventKind Kind,
    Id<ServiceItem>? ItemId,
    Id<Slide>? SlideId,
    CommandOrigin Origin);
