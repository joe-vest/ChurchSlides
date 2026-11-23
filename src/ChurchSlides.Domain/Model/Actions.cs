using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ChurchSlides.Domain.Model;

/// <summary>
/// Glossary "Action": a trigger, an optional delay and an ordered list of commands (ACT-001).
/// Named TriggeredAction in code to avoid System.Action.
/// </summary>
public sealed record TriggeredAction
{
    [JsonRequired] public ActionTrigger Trigger { get; init; } = null!;
    [JsonRequired] public TimeSpan Delay { get; init; } = TimeSpan.Zero;
    [JsonRequired] public ImmutableArray<Command> Commands { get; init; } = [];
}

/// <summary>Glossary "Trigger" (ACT-010..016). Named ActionTrigger in code to avoid System.Windows.Trigger.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ServiceStarted), "serviceStarted")]
[JsonDerivedType(typeof(ShowStarted), "showStarted")]
[JsonDerivedType(typeof(ShowEnded), "showEnded")]
[JsonDerivedType(typeof(SlideDisplayed), "slideDisplayed")]
[JsonDerivedType(typeof(SlideEnded), "slideEnded")]
[JsonDerivedType(typeof(TimerExpired), "timerExpired")]
[JsonDerivedType(typeof(AudioEnded), "audioEnded")]
public abstract record ActionTrigger;

public sealed record ServiceStarted : ActionTrigger;
public sealed record ShowStarted : ActionTrigger;
public sealed record ShowEnded : ActionTrigger;
public sealed record SlideDisplayed : ActionTrigger;
public sealed record SlideEnded : ActionTrigger;
public sealed record AudioEnded : ActionTrigger;

public sealed record TimerExpired : ActionTrigger
{
    [JsonRequired] public string TimerName { get; init; } = "";
}

/// <summary>Where GoToSlide goes. A null ItemId means "a slide within the current Show" (FRD ACT-022).</summary>
public sealed record SlideTarget(Id<Slide> SlideId, Id<ServiceItem>? ItemId = null);

/// <summary>One operation an Action performs (ACT-020..032).</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(NextSlide), "nextSlide")]
[JsonDerivedType(typeof(PreviousSlide), "previousSlide")]
[JsonDerivedType(typeof(NextItem), "nextItem")]
[JsonDerivedType(typeof(PreviousItem), "previousItem")]
[JsonDerivedType(typeof(GoToSlide), "goToSlide")]
[JsonDerivedType(typeof(GoToItem), "goToItem")]
[JsonDerivedType(typeof(StartAudio), "startAudio")]
[JsonDerivedType(typeof(StopAudio), "stopAudio")]
[JsonDerivedType(typeof(Clear), "clear")]
[JsonDerivedType(typeof(Black), "black")]
[JsonDerivedType(typeof(Logo), "logo")]
[JsonDerivedType(typeof(Normal), "normal")]
[JsonDerivedType(typeof(StartTimer), "startTimer")]
[JsonDerivedType(typeof(StopTimer), "stopTimer")]
public abstract record Command;

public sealed record NextSlide : Command;
public sealed record PreviousSlide : Command;
public sealed record NextItem : Command;
public sealed record PreviousItem : Command;
public sealed record StartAudio : Command;
public sealed record StopAudio : Command;
/// <summary>Hides all text. Sets the screen state, it doesn't toggle it (ACT-030).</summary>
public sealed record Clear : Command;

/// <summary>Shows black. Sets the screen state, it doesn't toggle it (ACT-031).</summary>
public sealed record Black : Command;

/// <summary>Shows the logo. Sets the screen state, it doesn't toggle it (ACT-032).</summary>
public sealed record Logo : Command;

/// <summary>Returns the screen to showing the current slide normally (ACT-033).</summary>
public sealed record Normal : Command;

public sealed record GoToSlide : Command
{
    [JsonRequired] public SlideTarget Target { get; init; } = null!;
}

public sealed record GoToItem : Command
{
    [JsonRequired] public Id<ServiceItem> ItemId { get; init; }
}

public sealed record StartTimer : Command
{
    [JsonRequired] public string TimerName { get; init; } = "";
}

public sealed record StopTimer : Command
{
    [JsonRequired] public string TimerName { get; init; } = "";
}
