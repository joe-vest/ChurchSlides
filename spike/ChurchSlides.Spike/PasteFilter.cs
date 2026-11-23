using System.Text.Json;
using System.Windows;
using System.Windows.Documents;
using ChurchSlides.Domain.Resolution;
using ChurchSlides.Domain.Text;
using Paragraph = ChurchSlides.Domain.Text.Paragraph;

namespace ChurchSlides.Spike;

/// <summary>What a paste keeps (C0.3 decision 3). Everything outside the five properties is always dropped.</summary>
public enum PasteMode
{
    KeepFive,
    BoldItalicOnly,
    PlainText,
}

/// <summary>A paste turned into a model fragment, with a note on what was dropped.</summary>
public sealed record PasteResult(RichText Fragment, string Note);

/// <summary>
/// Normalises pasted content into the model (C0.3 decision 3, TPL-024): our own copies come
/// back exactly; other rich text keeps only what the mode allows; <c>{{...}}</c> markup becomes
/// tokens.
/// </summary>
public static class PasteFilter
{
    /// <summary>The clipboard format our own copies carry: the model fragment as JSON.</summary>
    public const string ModelFormat = "ChurchSlides.RichText";

    public static string Label(PasteMode mode) => mode switch
    {
        PasteMode.KeepFive => "Keep font, size, color, bold, italic",
        PasteMode.BoldItalicOnly => "Keep bold and italic only",
        _ => "Plain text",
    };

    public static PasteResult Read(IDataObject data, PasteMode mode, BoxStyle style, double canvasHeight)
    {
        if (data.GetDataPresent(ModelFormat) && data.GetData(ModelFormat) is string json && ModelJson.Parse(json) is { } own)
            return new PasteResult(own, "copied in this editor: kept exactly");

        var rich = data.GetDataPresent(DataFormats.Xaml) ? DataFormats.Xaml
            : data.GetDataPresent(DataFormats.Rtf) ? DataFormats.Rtf
            : null;
        var plain = data.GetData(DataFormats.UnicodeText) as string ?? "";
        if (rich is null || mode == PasteMode.PlainText)
            return new PasteResult(FromPlain(plain), rich is null ? "plain text" : $"plain text; all formatting dropped ({rich})");

        var doc = FlowConverter.ToDocument(RichText.Empty, style, canvasHeight, ChipDesign.TaggedRun);
        using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(data.GetData(rich) as string ?? "")))
            new TextRange(doc.ContentStart, doc.ContentEnd).Load(stream, rich);
        var dropped = Extras(doc);
        var model = FlowConverter.ToModel(doc, canvasHeight);

        if (mode == PasteMode.BoldItalicOnly)
        {
            var stripped = model.Paragraphs.SelectMany(p => p.Inlines).Count(i => i.Format is { } f && (f.FontFamily ?? f.ColorHex ?? (object?)f.FontSize) is not null);
            if (stripped > 0)
                dropped.Add($"font, size or color on {stripped} runs");
            model = model with
            {
                Paragraphs = [.. model.Paragraphs.Select(p => p with
                {
                    Inlines = FlowConverter.Merge(p.Inlines.Select(i => i with { Format = BoldItalic(i.Format) })),
                })],
            };
        }
        model = FlowConverter.Normalize(model);
        var explicitRuns = model.Paragraphs.SelectMany(p => p.Inlines).Count(i => i.Format is not null);
        var note = $"{rich}; {explicitRuns} runs keep explicit formatting" + (dropped.Count == 0 ? "" : $"; dropped: {string.Join(", ", dropped)}");
        return new PasteResult(model, note);
    }

    private static RichText FromPlain(string text) => new()
    {
        Paragraphs = [.. text.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n').Select(line => new Paragraph { Inlines = TokenMarkup.Parse(line) })],
    };

    private static RunFormat? BoldItalic(RunFormat? f)
    {
        if (f is null)
            return null;
        var kept = new RunFormat { Bold = f.Bold, Italic = f.Italic };
        return kept == RunFormat.None ? null : kept;
    }

    /// <summary>What the pasted document had that the model can't hold.</summary>
    private static List<string> Extras(FlowDocument doc)
    {
        int underline = 0, highlight = 0, lists = 0, tables = 0, images = 0, links = 0, layout = 0;
        void Visit(TextElement e)
        {
            if (e.ReadLocalValue(System.Windows.Documents.Inline.TextDecorationsProperty) is TextDecorationCollection { Count: > 0 }
                || e.ReadLocalValue(System.Windows.Documents.Paragraph.TextDecorationsProperty) is TextDecorationCollection { Count: > 0 })
                underline++;
            if (e is System.Windows.Documents.Inline && e.ReadLocalValue(TextElement.BackgroundProperty) != DependencyProperty.UnsetValue)
                highlight++;
            if (e is Block && (e.ReadLocalValue(Block.TextAlignmentProperty) != DependencyProperty.UnsetValue
                || e.ReadLocalValue(Block.MarginProperty) != DependencyProperty.UnsetValue
                || e.ReadLocalValue(Block.LineHeightProperty) != DependencyProperty.UnsetValue))
                layout++;
            switch (e)
            {
                case List:
                    lists++;
                    break;
                case Table:
                    tables++;
                    break;
                case BlockUIContainer or InlineUIContainer:
                    images++;
                    break;
                case Hyperlink:
                    links++;
                    break;
            }
        }

        foreach (var element in Descendants(doc))
            Visit(element);

        var dropped = new List<string>();
        void Count(int n, string what)
        {
            if (n > 0)
                dropped.Add($"{what} ({n})");
        }
        Count(underline, "underline");
        Count(highlight, "highlight");
        Count(layout, "paragraph alignment or spacing");
        Count(lists, "lists");
        Count(tables, "tables");
        Count(images, "images or objects");
        Count(links, "links");
        return dropped;
    }

    private static IEnumerable<TextElement> Descendants(FlowDocument doc)
    {
        for (var p = doc.ContentStart; p is not null && p.CompareTo(doc.ContentEnd) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart && p.GetAdjacentElement(LogicalDirection.Forward) is TextElement e)
                yield return e;
        }
    }
}
