using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Resolution;
using ChurchSlides.Domain.Text;
using WpfInline = System.Windows.Documents.Inline;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace ChurchSlides.Spike;

/// <summary>How a token shows in the editor (C0.3 decision 2).</summary>
public enum ChipDesign
{
    /// <summary>An InlineUIContainer holding a small bordered TextBlock: atomic for free.</summary>
    Container,

    /// <summary>A plain Run with a shaded background, kept atomic by the editor's guards.</summary>
    TaggedRun,
}

/// <summary>A Text Box's settings for one sample: Template defaults plus the box's own overrides.</summary>
public sealed record BoxStyle(TextDefaults Defaults, RectRelative Geometry)
{
    public static readonly RectRelative StandardGeometry = new(0.07, 0.08, 0.86, 0.84);
}

/// <summary>
/// RichText ⇄ FlowDocument (D-3). The FlowDocument is only the editing surface. The document
/// carries the Template defaults, so a run reads back only what was set on it or on an element
/// below the document (TPL-024). Throwaway: C2.3 rewrites this test-first.
/// </summary>
public static class FlowConverter
{
    /// <summary>
    /// Shading for a token in the editor (SLD-027: recognizable markers). A new brush per token:
    /// the control merges neighbouring runs whose formatting values are equal, and brushes compare
    /// by reference, so two tagged runs side by side would otherwise become one run.
    /// </summary>
    public static Brush NewChipShade() => Frozen(new SolidColorBrush(Color.FromArgb(110, 90, 160, 255)));

    private static readonly Brush ChipBorder = Frozen(new SolidColorBrush(Color.FromArgb(200, 120, 180, 255)));

    // ---- Model → document ----

    public static FlowDocument ToDocument(RichText text, BoxStyle style, double canvasHeight, ChipDesign chips)
    {
        var d = style.Defaults;
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily(d.FontFamily),
            FontSize = d.FontSizeRelative * canvasHeight,
            Foreground = BrushOf(d.ColorHex),
            FontWeight = d.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = d.Italic ? FontStyles.Italic : FontStyles.Normal,
            TextAlignment = Alignment(d.HAlign),
        };
        TextOptions.SetTextFormattingMode(doc, TextFormattingMode.Ideal);
        if (UniformLineHeight(d, canvasHeight) is { } lineHeight)
        {
            doc.LineHeight = lineHeight;
            doc.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        }

        foreach (var p in text.Paragraphs)
        {
            var paragraph = new WpfParagraph { Margin = new Thickness(0) };
            foreach (var inline in p.Inlines)
            {
                if (ToInline(inline, canvasHeight, chips) is { } wpf)
                    paragraph.Inlines.Add(wpf);
            }
            doc.Blocks.Add(paragraph);
        }
        return doc;
    }

    /// <summary>
    /// The line height both the editor and the renderer use when line spacing isn't 1, or null for
    /// the font's own line height (SLD-023). One rule for both, so they break and stack lines alike.
    /// </summary>
    public static double? UniformLineHeight(TextDefaults d, double canvasHeight) =>
        Math.Abs(d.LineSpacing - 1.0) < 1e-9
            ? null
            : d.FontSizeRelative * canvasHeight * new FontFamily(d.FontFamily).LineSpacing * d.LineSpacing;

    public static TextAlignment Alignment(HorizontalAlign align) => align switch
    {
        HorizontalAlign.Left => TextAlignment.Left,
        HorizontalAlign.Right => TextAlignment.Right,
        _ => TextAlignment.Center,
    };

    private static WpfInline? ToInline(ChurchSlides.Domain.Text.Inline inline, double canvasHeight, ChipDesign chips)
    {
        WpfInline wpf;
        switch (inline)
        {
            case TextRun { Text.Length: 0 }:
                return null;
            case TextRun run:
                wpf = new Run(run.Text);
                break;
            case TokenRun token:
                wpf = NewChip(token.Token, chips);
                break;
            default:
                return null;
        }
        ApplyFormat(wpf, inline.Format, canvasHeight);
        return wpf;
    }

    /// <summary>A token as shown in the editor. The token itself rides in Tag; the text is its markup.</summary>
    public static WpfInline NewChip(Token token, ChipDesign chips)
    {
        var markup = token.ToMarkup();
        if (chips == ChipDesign.TaggedRun)
            return new Run(markup) { Tag = token, Background = NewChipShade() };

        // The TextBlock inherits font properties from the container, so formatting the chip shows.
        var label = new TextBlock { Text = markup, Tag = token };
        var border = new Border
        {
            Child = label,
            Background = NewChipShade(),
            BorderBrush = ChipBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
        };
        return new InlineUIContainer(border) { Tag = token, BaselineAlignment = BaselineAlignment.Baseline };
    }

    public static void ApplyFormat(TextElement element, RunFormat? format, double canvasHeight)
    {
        if (format is null)
            return;
        if (format.FontFamily is { } family)
            element.FontFamily = new FontFamily(family);
        if (format.FontSize is { } size)
            element.FontSize = size * canvasHeight;
        if (format.ColorHex is { } color)
            element.Foreground = BrushOf(color);
        if (format.Bold is { } bold)
            element.FontWeight = bold ? FontWeights.Bold : FontWeights.Normal;
        if (format.Italic is { } italic)
            element.FontStyle = italic ? FontStyles.Italic : FontStyles.Normal;
    }

    // ---- Document → model ----

    public static RichText ToModel(FlowDocument doc, double canvasHeight)
    {
        var paragraphs = new List<ChurchSlides.Domain.Text.Paragraph>();
        foreach (var block in AllParagraphs(doc.Blocks))
        {
            // A LineBreak (Shift+Enter) splits the paragraph: the model has no soft breaks.
            var current = new List<ChurchSlides.Domain.Text.Inline>();
            foreach (var piece in Flatten(block.Inlines))
            {
                if (piece is null)
                {
                    paragraphs.Add(new ChurchSlides.Domain.Text.Paragraph { Inlines = Merge(current) });
                    current = [];
                }
                else
                {
                    current.AddRange(ReadInline(piece, canvasHeight));
                }
            }
            paragraphs.Add(new ChurchSlides.Domain.Text.Paragraph { Inlines = Merge(current) });
        }

        // An empty editor always holds one empty paragraph; the model calls that Empty.
        if (paragraphs.Count == 1 && paragraphs[0].Inlines.Length == 0)
            return RichText.Empty;
        return new RichText { Paragraphs = [.. paragraphs] };
    }

    /// <summary>Paragraphs in order, including any inside lists or sections a paste brought in.</summary>
    public static IEnumerable<WpfParagraph> AllParagraphs(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case WpfParagraph p:
                    yield return p;
                    break;
                case Section s:
                    foreach (var p in AllParagraphs(s.Blocks))
                        yield return p;
                    break;
                case List l:
                    foreach (var item in l.ListItems)
                    {
                        foreach (var p in AllParagraphs(item.Blocks))
                            yield return p;
                    }
                    break;
            }
        }
    }

    /// <summary>Leaf inlines in order, Spans opened; null marks a LineBreak.</summary>
    private static IEnumerable<WpfInline?> Flatten(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case LineBreak:
                    yield return null;
                    break;
                case Span span:
                    foreach (var child in Flatten(span.Inlines))
                        yield return child;
                    break;
                default:
                    yield return inline;
                    break;
            }
        }
    }

    /// <summary>
    /// An editor inline as model inlines. Token markup in text is read as tokens (as on import,
    /// with the Domain's own parser): the control merges neighbouring tagged runs with equal
    /// formatting into one run, and a paste brings tokens back as plain markup.
    /// </summary>
    private static IEnumerable<ChurchSlides.Domain.Text.Inline> ReadInline(WpfInline inline, double canvasHeight)
    {
        var format = ReadFormat(inline, canvasHeight);
        if (TokenOf(inline) is { } token)
            return [new TokenRun { Token = token, Format = format }];
        var text = inline switch
        {
            Run run => run.Text,
            InlineUIContainer { Child: Border { Child: TextBlock label } } => label.Text, // a chip that lost its Tag
            _ => "",
        };
        return TokenMarkup.Parse(text).Select(i => i with { Format = format });
    }

    /// <summary>The token an editor inline stands for, if it still is one.</summary>
    public static Token? TokenOf(WpfInline inline) => inline switch
    {
        Run { Tag: Token token } run when run.Text == token.ToMarkup() => token,
        InlineUIContainer { Tag: Token token } => token,
        InlineUIContainer { Child: Border { Child: TextBlock label } } => SingleToken(label.Text),
        _ => null,
    };

    /// <summary>The token if the text is exactly one token's markup.</summary>
    private static Token? SingleToken(string text) =>
        TokenMarkup.Parse(text) is [TokenRun only] ? only.Token : null;

    /// <summary>
    /// Formatting set on the inline or an element between it and the document (the control puts
    /// whole-paragraph formatting on the Paragraph or a Span). The nearest local value wins.
    /// </summary>
    private static RunFormat? ReadFormat(WpfInline inline, double canvasHeight)
    {
        // FlowDocument isn't a TextElement, so the walk stops below it.
        object? Local(DependencyProperty property)
        {
            for (var e = (TextElement)inline; e is not null; e = e.Parent as TextElement)
            {
                var value = e.ReadLocalValue(property);
                if (value != DependencyProperty.UnsetValue)
                    return value;
            }
            return null;
        }

        var format = new RunFormat
        {
            FontFamily = (Local(TextElement.FontFamilyProperty) as FontFamily)?.Source,
            FontSize = Local(TextElement.FontSizeProperty) is double size ? Math.Round(size / canvasHeight, 6) : null,
            ColorHex = Local(TextElement.ForegroundProperty) is SolidColorBrush brush ? HexOf(brush.Color) : null,
            Bold = Local(TextElement.FontWeightProperty) is FontWeight w ? w.ToOpenTypeWeight() >= 600 : null,
            Italic = Local(TextElement.FontStyleProperty) is FontStyle s ? s != FontStyles.Normal : null,
        };
        return format == RunFormat.None ? null : format;
    }

    /// <summary>Joins neighbouring text runs with equal formatting and drops empty ones (the control splits runs freely).</summary>
    public static System.Collections.Immutable.ImmutableArray<ChurchSlides.Domain.Text.Inline> Merge(IEnumerable<ChurchSlides.Domain.Text.Inline> inlines)
    {
        var merged = new List<ChurchSlides.Domain.Text.Inline>();
        foreach (var inline in inlines)
        {
            if (inline is TextRun { Text.Length: 0 })
                continue;
            if (inline is TextRun run && merged.Count > 0 && merged[^1] is TextRun last && last.Format == run.Format)
                merged[^1] = last with { Text = last.Text + run.Text };
            else
                merged.Add(inline);
        }
        return [.. merged];
    }

    /// <summary>The model's normal form: merged runs, and Empty for a lone empty paragraph.</summary>
    public static RichText Normalize(RichText text)
    {
        var paragraphs = text.Paragraphs.Select(p => p with { Inlines = Merge(p.Inlines) }).ToList();
        return paragraphs.Count == 1 && paragraphs[0].Inlines.Length == 0
            ? RichText.Empty
            : new RichText { Paragraphs = [.. paragraphs] };
    }

    // ---- Colors ----

    public static SolidColorBrush BrushOf(string hex) => Frozen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)));

    public static string HexOf(Color c) => c.A == 255
        ? string.Create(CultureInfo.InvariantCulture, $"#{c.R:X2}{c.G:X2}{c.B:X2}")
        : string.Create(CultureInfo.InvariantCulture, $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}");

    private static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
