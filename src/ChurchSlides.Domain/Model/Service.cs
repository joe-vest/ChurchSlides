using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ChurchSlides.Domain.Model;

public enum ServiceMode { Rehearse, Live }

public enum TimerType { CountdownToClockTime, CountdownDuration }

public enum TimerEndBehavior { HoldAtZero, ContinueNegative }

/// <summary>One dated run order (DEC-001).</summary>
public sealed record Service
{
    [JsonRequired] public Id<Service> Id { get; init; }
    [JsonRequired] public string Name { get; init; } = "";
    [JsonRequired] public DateOnly Date { get; init; }
    [JsonRequired] public ServiceMode Mode { get; init; } = ServiceMode.Rehearse;
    [JsonRequired] public ImmutableArray<ServiceItem> Items { get; init; } = [];
    [JsonRequired] public ImmutableArray<Variable> Variables { get; init; } = [];
    [JsonRequired] public ImmutableArray<CountdownTimer> Timers { get; init; } = [];
    [JsonRequired] public ImmutableArray<TriggeredAction> Actions { get; init; } = [];
    public Id<ServiceTemplate>? CreatedFromServiceTemplateId { get; init; }
}

/// <summary>
/// One entry in a Service. Exactly one of four forms (DEC-004, DEC-006).
/// Item-level Actions belong to the Service, not to the catalog Show (ACT-002).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ShowReferenceItem), "showReference")]
[JsonDerivedType(typeof(ShowCopyItem), "showCopy")]
[JsonDerivedType(typeof(PlaceholderItem), "placeholder")]
[JsonDerivedType(typeof(ScriptureItem), "scripture")]
public abstract record ServiceItem
{
    [JsonRequired, JsonPropertyOrder(-1)] // first in the file, next to "$type"
    public Id<ServiceItem> Id { get; init; }

    /// <summary>Reserved (TPL-011, COULD).</summary>
    public Id<Template>? TemplateOverrideId { get; init; }

    [JsonRequired] public ImmutableArray<TriggeredAction> Actions { get; init; } = [];
}

/// <summary>
/// A Service Item that plays a Show: either a reference to a catalog Show or a Service-local copy.
/// The skip list is the single source of truth for which slides Next/Previous pass over (SVC-021, SVC-027).
/// </summary>
public abstract record ShowItem : ServiceItem
{
    [JsonRequired] public ImmutableArray<Id<Slide>> SkippedSlideIds { get; init; } = [];

    /// <summary>Set when this item was a Placeholder that got filled, so it can be cleared back (SVC-033).</summary>
    public PlaceholderSpec? FilledFrom { get; init; }

    public abstract ShowItem WithSkippedSlides(ImmutableArray<Id<Slide>> skipped);
}

/// <summary>Points at a catalog Show, optionally skipping some of its slides (SVC-020, SVC-021).</summary>
public sealed record ShowReferenceItem : ShowItem
{
    [JsonRequired] public Id<Show> ShowId { get; init; }

    public override ShowItem WithSkippedSlides(ImmutableArray<Id<Slide>> skipped) => this with { SkippedSlideIds = skipped };
}

/// <summary>A Service-local copy of a Show, created by editing a reference (SVC-022). Slide IDs are preserved from the origin.</summary>
public sealed record ShowCopyItem : ShowItem
{
    [JsonRequired] public Show LocalShow { get; init; } = null!;

    public override ShowItem WithSkippedSlides(ImmutableArray<Id<Slide>> skipped) => this with { SkippedSlideIds = skipped };

    /// <summary>The catalog Show this was forked from, for "Revert to catalog version" (SVC-024).</summary>
    public Id<Show>? OriginShowId { get; init; }
}

/// <summary>An unfilled slot, optionally restricted by Category and/or Kind (SVC-031).</summary>
public sealed record PlaceholderItem : ServiceItem
{
    [JsonRequired] public PlaceholderSpec Placeholder { get; init; } = null!;
}

/// <summary>Generates ordinary Service-local slides from a Bible passage (SCR-008). Generated is null until generated.</summary>
public sealed record ScriptureItem : ServiceItem
{
    [JsonRequired] public ScriptureSpec Scripture { get; init; } = null!;
    public Show? Generated { get; init; }
}

public sealed record PlaceholderSpec
{
    [JsonRequired] public string Label { get; init; } = "";
    public Id<Category>? CategoryFilter { get; init; }
    public ShowKind? KindFilter { get; init; }
}

public sealed record ScriptureSpec
{
    [JsonRequired] public string BibleId { get; init; } = "";
    [JsonRequired] public string Reference { get; init; } = "";
    [JsonRequired] public int VersesPerSlide { get; init; } = 2;
    [JsonRequired] public bool ShowVerseNumbers { get; init; } = true;
    [JsonRequired] public bool ShowReferenceLine { get; init; } = true;
    public Id<Template>? TemplateId { get; init; }
}

/// <summary>A named value defined on a Service (DEC-009). Slides reference it with a token.</summary>
public sealed record Variable
{
    [JsonRequired] public string Name { get; init; } = "";
    [JsonRequired] public string Value { get; init; } = "";
}

/// <summary>Glossary "Timer" (TMR-001). Named CountdownTimer in code to avoid System.Threading.Timer.</summary>
public sealed record CountdownTimer
{
    [JsonRequired] public string Name { get; init; } = "";
    [JsonRequired] public TimerType Type { get; init; }
    public TimeOnly? TargetClockTime { get; init; }
    public TimeSpan? Duration { get; init; }
    [JsonRequired] public TimerEndBehavior EndBehavior { get; init; } = TimerEndBehavior.HoldAtZero;
    [JsonRequired] public int WarningThresholdSeconds { get; init; }
    public string? WarningColorHex { get; init; }
    [JsonRequired] public bool AutoStartOnDisplay { get; init; } = true;
}

/// <summary>A blueprint for new Services (SVC-030). Changing it never changes existing Services (SVC-035).</summary>
public sealed record ServiceTemplate
{
    [JsonRequired] public Id<ServiceTemplate> Id { get; init; }
    [JsonRequired] public string Name { get; init; } = "";
    [JsonRequired] public ImmutableArray<ServiceItem> Items { get; init; } = [];
    [JsonRequired] public ImmutableArray<Variable> Variables { get; init; } = [];
    [JsonRequired] public ImmutableArray<CountdownTimer> Timers { get; init; } = [];
    [JsonRequired] public ImmutableArray<TriggeredAction> Actions { get; init; } = [];
}
