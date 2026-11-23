using System.Collections.Immutable;
using ChurchSlides.Domain.Model;

namespace ChurchSlides.Domain.Resolution;

/// <summary>
/// Everything a renderer needs to draw one slide, with all Template defaults, inline formatting,
/// Variables and metadata already resolved. Immutable, so it can be cached and shared between threads.
/// </summary>
public sealed record RenderModel
{
    public required Id<Template> TemplateId { get; init; }
    public required Background Background { get; init; }
    public ImmutableArray<RenderTextBox> TextBoxes { get; init; } = [];

    /// <summary>Things that look wrong but were handled. The pre-service check reads this list.</summary>
    public ImmutableArray<RenderProblem> Problems { get; init; } = [];
}

public sealed record RenderTextBox
{
    /// <summary>The Slide's text box this came from. Null for the generated copyright box.</summary>
    public Id<SlideTextBox>? SourceId { get; init; }

    public bool IsCopyright { get; init; }
    public required RectRelative Geometry { get; init; }
    public required HorizontalAlign HAlign { get; init; }
    public required VerticalAlign VAlign { get; init; }
    public required double LineSpacing { get; init; }
    public Outline? Outline { get; init; }
    public Shadow? Shadow { get; init; }
    public ImmutableArray<RenderParagraph> Paragraphs { get; init; } = [];
}

public sealed record RenderParagraph
{
    public ImmutableArray<RenderRun> Runs { get; init; } = [];
}

/// <summary>Fully resolved text style. Font size is a fraction of canvas height (SLD-005).</summary>
public sealed record RunStyle
{
    public required string FontFamily { get; init; }
    public required double FontSize { get; init; }
    public required string ColorHex { get; init; }
    public required bool Bold { get; init; }
    public required bool Italic { get; init; }
}

public abstract record RenderRun
{
    public required RunStyle Style { get; init; }
}

public sealed record RenderTextRun : RenderRun
{
    public required string Text { get; init; }
}

/// <summary>A live Timer value. The renderer fills in the text each second (TMR-010) and applies the warning color.</summary>
public sealed record RenderTimerRun : RenderRun
{
    public required string TimerName { get; init; }
    public int WarningThresholdSeconds { get; init; }
    public string? WarningColorHex { get; init; }
}
