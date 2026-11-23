using ChurchSlides.Domain;
using ChurchSlides.Domain.Model;

namespace ChurchSlides.Engine;

/// <summary>
/// Who issued a command. Operator: buttons, hotkeys, clicker, phone remote. Automation: Actions and Cues.
/// An operator change of Service Item stops audio, an automated one doesn't (DEC-020, ARCH 6.5).
/// </summary>
public enum CommandOrigin { Operator, Automation }

/// <summary>What the Output shows on top of the current slide (LIVE-013). Normal shows the slide.</summary>
public enum ScreenState { Normal, Black, Clear, Logo }

/// <summary>
/// The one command API (ARCH 6.1, FUT-004). Operator input, the phone remote and Actions all call it, so they follow
/// the same rules. While not presenting only <see cref="Select"/> and <see cref="StartPresenting"/> do anything (DEC-021).
/// Going to the current slide does nothing (ACT-013). Unknown targets do nothing (ACT-041).
/// </summary>
public interface ICommandApi
{
    /// <summary>The nearest displayable slide after the current one, crossing items; nothing at the end (LIVE-005, LIVE-007).</summary>
    void Next(CommandOrigin origin);

    /// <summary>The nearest displayable slide before the current one; nothing at the start (LIVE-005, LIVE-007).</summary>
    void Previous(CommandOrigin origin);

    /// <summary>The first displayable slide of the nearest later item that has one (LIVE-016).</summary>
    void NextItem(CommandOrigin origin);

    /// <summary>The first displayable slide of the nearest earlier item that has one, even from mid-item (LIVE-016).</summary>
    void PreviousItem(CommandOrigin origin);

    /// <summary>A slide, even a Skipped one (LIVE-017). No item means the current item (ACT-022).</summary>
    void GoToSlide(SlideTarget target, CommandOrigin origin);

    /// <summary>An item's first displayable slide (an item header click). An item with none: nothing (LIVE-006).</summary>
    void GoToItem(Id<ServiceItem> itemId, CommandOrigin origin);

    /// <summary>Operator Black / Clear / Logo: on, or back to Normal if it was already on. One at a time (LIVE-013).</summary>
    void ToggleScreen(ScreenState screen, CommandOrigin origin);

    /// <summary>Action screen commands: set, never toggle (ACT-026..028, ACT-033).</summary>
    void SetScreen(ScreenState screen, CommandOrigin origin);

    /// <summary>Not presenting: select a slide, or with no slide the item's first displayable slide (DEC-021).</summary>
    void Select(Id<ServiceItem> itemId, Id<Slide>? slideId, CommandOrigin origin);

    /// <summary>Go Live or Rehearse: show the selection, or the first displayable slide (LIVE-009, MODE-005).</summary>
    void StartPresenting(ServiceMode mode, CommandOrigin origin);

    /// <summary>Back to not presenting, with the last slide selected (LIVE-018).</summary>
    void StopPresenting(CommandOrigin origin);
}
