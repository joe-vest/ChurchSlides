using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Text;

namespace ChurchSlides.Spike;

/// <summary>A laid-out Text Box: the FormattedText, its plain text and where it sits in the box.</summary>
public sealed record TextLayout(FormattedText Text, string Plain, Point Origin, bool Overflows);

/// <summary>
/// Draws a Text Box's RichText with one FormattedText and range setters (D-4, ARCH 7.2): Template
/// defaults for unset properties, alignment, line spacing, an overflow flag (SLD-028) and an
/// optional geometry outline (SLD-025, cost only). Throwaway: C2.1 rewrites this test-first.
/// </summary>
public static class RichTextRenderer
{
    /// <summary>How tokens read in the renderer: as their markup (to compare with the editor) or resolved.</summary>
    public static string Markup(Token token) => token.ToMarkup();

    /// <summary>Stand-in values for the eye check (VAR-006: formatting applies to the value).</summary>
    public static string SampleValue(Token token) => token switch
    {
        VariableToken v => v.Name switch
        {
            "preacher" => "Pastor Ruth Okafor",
            "sermonTitle" => "Grace upon grace",
            _ => $"[{v.Name}]",
        },
        TimerToken => "0:05:00",
        ShowMetadataToken { Field: ShowMetadataField.Title } => "Amazing Grace",
        ShowMetadataToken { Field: ShowMetadataField.Authors } => "John Newton",
        ShowMetadataToken { Field: ShowMetadataField.Copyright } => "Public domain",
        ShowMetadataToken { Field: ShowMetadataField.Ccli } => "22025",
        ChurchLicenseToken => "CCLI licence 1234567",
        _ => token.ToMarkup(),
    };

    public static TextLayout Layout(RichText text, BoxStyle style, Size box, double canvasHeight, Func<Token, string> tokenText)
    {
        var d = style.Defaults;
        var plain = new System.Text.StringBuilder();
        var ranges = new List<(int Start, int Length, RunFormat Format)>();
        for (var i = 0; i < text.Paragraphs.Length; i++)
        {
            if (i > 0)
                plain.Append('\n');
            foreach (var inline in text.Paragraphs[i].Inlines)
            {
                var piece = inline switch
                {
                    TextRun run => run.Text,
                    TokenRun token => tokenText(token.Token),
                    _ => "",
                };
                if (inline.Format is { } format && piece.Length > 0)
                    ranges.Add((plain.Length, piece.Length, format));
                plain.Append(piece);
            }
        }

        var typeface = new Typeface(new FontFamily(d.FontFamily), d.Italic ? FontStyles.Italic : FontStyles.Normal,
            d.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
        var ft = new FormattedText(plain.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface,
            d.FontSizeRelative * canvasHeight, FlowConverter.BrushOf(d.ColorHex), 1.0)
        {
            MaxTextWidth = box.Width,
            TextAlignment = FlowConverter.Alignment(d.HAlign),
            Trimming = TextTrimming.None, // the default is WordEllipsis, which the editor never does
        };
        if (FlowConverter.UniformLineHeight(d, canvasHeight) is { } lineHeight)
            ft.LineHeight = lineHeight;

        foreach (var (start, length, f) in ranges)
        {
            if (f.FontFamily is { } family)
                ft.SetFontFamily(family, start, length);
            if (f.FontSize is { } size)
                ft.SetFontSize(size * canvasHeight, start, length);
            if (f.ColorHex is { } color)
                ft.SetForegroundBrush(FlowConverter.BrushOf(color), start, length);
            if (f.Bold is { } bold)
                ft.SetFontWeight(bold ? FontWeights.Bold : FontWeights.Normal, start, length);
            if (f.Italic is { } italic)
                ft.SetFontStyle(italic ? FontStyles.Italic : FontStyles.Normal, start, length);
        }

        var origin = new Point(0, VerticalOffset(d.VAlign, box.Height, ft.Height));
        var overflows = ft.Height > box.Height + 0.5 || ft.Width > box.Width + 0.5;
        return new TextLayout(ft, plain.ToString(), origin, overflows);
    }

    /// <summary>Where the text block's top goes in the box. Overflowing text starts at the top in both the editor and here.</summary>
    public static double VerticalOffset(VerticalAlign align, double boxHeight, double textHeight)
    {
        var spare = Math.Max(0, boxHeight - textHeight);
        return align switch
        {
            VerticalAlign.Top => 0,
            VerticalAlign.Bottom => spare,
            _ => spare / 2,
        };
    }

    /// <summary>Draws the laid-out text at <paramref name="offset"/>, with a geometry outline if asked.</summary>
    public static void Draw(DrawingContext dc, TextLayout layout, Vector offset, double outlineThickness = 0)
    {
        var origin = layout.Origin + offset;
        if (outlineThickness > 0)
        {
            var pen = new Pen(Brushes.Black, outlineThickness) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            dc.DrawGeometry(null, pen, layout.Text.BuildGeometry(origin));
        }
        dc.DrawText(layout.Text, origin);
    }

    /// <summary>
    /// Character offsets (in <see cref="TextLayout.Plain"/>) where each line starts. Slow
    /// (a highlight geometry per character): for the comparison only, never timed.
    /// </summary>
    public static List<int> LineStarts(TextLayout layout)
    {
        var plain = layout.Plain;
        var starts = new List<int> { 0 };
        Rect? previous = null;
        for (var i = 0; i < plain.Length; i++)
        {
            if (plain[i] == '\n')
            {
                starts.Add(i + 1);
                previous = null;
                continue;
            }
            if (layout.Text.BuildHighlightGeometry(default, i, 1) is not { } geometry)
                continue;
            var bounds = geometry.Bounds;
            if (previous is { } p && bounds.Top >= p.Bottom - 1 && starts[^1] != i)
                starts.Add(i);
            previous = bounds;
        }
        return starts;
    }
}
