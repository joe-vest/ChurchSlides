using System.Text.Json.Serialization;
using ChurchSlides.Domain.Text;

namespace ChurchSlides.Domain.Model;

/// <summary>A rectangle as fractions (0..1) of the canvas (SLD-001).</summary>
public sealed record RectRelative(double X, double Y, double Width, double Height);

public enum BackgroundFit { Fit, Fill, Stretch }

/// <summary>Image or solid color. Video is reserved for the future (SLD-009, FUT-001).</summary>
public enum MediaKind { Image, SolidColor }

public enum HorizontalAlign { Left, Center, Right }

public enum VerticalAlign { Top, Middle, Bottom }

public sealed record Background
{
    [JsonRequired] public MediaKind Kind { get; init; } = MediaKind.SolidColor;
    public LibraryRelativePath? MediaPath { get; init; }
    public string? ColorHex { get; init; }
    [JsonRequired] public BackgroundFit Fit { get; init; } = BackgroundFit.Fill;
}

/// <summary>Outline thickness is a fraction of canvas height. (SLD-025, COULD.)</summary>
public sealed record Outline(string ColorHex, double Thickness);

/// <summary>Offsets and blur are fractions of canvas height. (SLD-025, COULD.)</summary>
public sealed record Shadow(string ColorHex, double OffsetX, double OffsetY, double Blur);

/// <summary>Default text formatting for a Template Box. Size is a fraction of canvas height (SLD-005).</summary>
public sealed record TextDefaults
{
    [JsonRequired] public string FontFamily { get; init; } = "Segoe UI";
    [JsonRequired] public double FontSizeRelative { get; init; } = 0.07;
    [JsonRequired] public string ColorHex { get; init; } = "#FFFFFF";
    [JsonRequired] public bool Bold { get; init; }
    [JsonRequired] public bool Italic { get; init; }
    [JsonRequired] public HorizontalAlign HAlign { get; init; } = HorizontalAlign.Center;
    [JsonRequired] public VerticalAlign VAlign { get; init; } = VerticalAlign.Middle;
    [JsonRequired] public double LineSpacing { get; init; } = 1.0;
    public Outline? Outline { get; init; }
    public Shadow? Shadow { get; init; }
}

/// <summary>
/// A positioned rich-text region on a Slide (glossary: Text Box). Bound to a Template Box by name,
/// or detached with its own geometry (TPL-020..023).
/// </summary>
public sealed record SlideTextBox
{
    [JsonRequired] public Id<SlideTextBox> Id { get; init; }

    /// <summary>
    /// The Template Box this text box takes its defaults (and, unless detached, its geometry) from.
    /// Kept when detached, so "Reattach" knows where to go back to (TPL-022, TPL-023).
    /// </summary>
    public string? TemplateBoxName { get; init; }

    /// <summary>A non-null geometry means this text box is detached and uses its own position.</summary>
    public RectRelative? Geometry { get; init; }

    [JsonIgnore] public bool IsDetached => Geometry is not null;

    [JsonRequired] public RichText Content { get; init; } = RichText.Empty;

    // Per-box overrides of Template defaults (SLD-023).
    public HorizontalAlign? HAlign { get; init; }
    public VerticalAlign? VAlign { get; init; }
    public double? LineSpacing { get; init; }
}
