using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfInline = System.Windows.Documents.Inline;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace ChurchSlides.Spike;

/// <summary>
/// Positions in the editor as character offsets in the model's plain text (paragraphs joined by
/// '\n', tokens as their markup), so editor and renderer line breaks can be compared, and
/// scripted edits can name ranges. Also rasterizing and pixel comparison.
/// </summary>
public static class EditorText
{
    public static int Offset(FlowDocument doc, TextPointer target)
    {
        var offset = 0;
        var first = true;
        foreach (var paragraph in FlowConverter.AllParagraphs(doc.Blocks))
        {
            if (!first)
                offset++;
            first = false;
            if (target.CompareTo(paragraph.ContentEnd) > 0)
            {
                offset += Length(paragraph);
                continue;
            }
            foreach (var leaf in Leaves(paragraph.Inlines))
            {
                if (target.CompareTo(leaf.ElementEnd) >= 0)
                {
                    offset += LeafLength(leaf);
                    continue;
                }
                if (leaf is Run run && target.CompareTo(run.ContentStart) > 0)
                    offset += run.ContentStart.GetOffsetToPosition(target);
                return offset;
            }
            return offset;
        }
        return offset;
    }

    /// <summary>The position at a plain-text offset. At a boundary between inlines it is the end of the earlier one (as typing would be).</summary>
    public static TextPointer PointerAt(FlowDocument doc, int offset)
    {
        TextPointer? last = null;
        foreach (var paragraph in FlowConverter.AllParagraphs(doc.Blocks))
        {
            last = paragraph.ContentEnd;
            var length = Length(paragraph);
            if (offset > length)
            {
                offset -= length + 1;
                continue;
            }
            if (offset == 0)
                return paragraph.ContentStart;
            foreach (var leaf in Leaves(paragraph.Inlines))
            {
                var leafLength = LeafLength(leaf);
                if (offset <= leafLength)
                {
                    return leaf is Run run
                        ? run.ContentStart.GetPositionAtOffset(offset)
                        : offset * 2 < leafLength ? leaf.ElementStart : leaf.ElementEnd; // inside a chip: the nearer edge
                }
                offset -= leafLength;
            }
            return paragraph.ContentEnd;
        }
        return last ?? doc.ContentEnd;
    }

    /// <summary>Where each line starts, as the editor laid it out. Needs the editor to have been laid out.</summary>
    public static List<int> LineStarts(RichTextBox editor)
    {
        var doc = editor.Document;
        var starts = new List<int>();
        var line = doc.ContentStart.GetLineStartPosition(0);
        while (line is not null)
        {
            starts.Add(Offset(doc, line));
            var next = line.GetLineStartPosition(1, out var moved);
            if (moved == 0)
                break;
            line = next;
        }
        return starts;
    }

    private static int Length(WpfParagraph paragraph) => Leaves(paragraph.Inlines).Sum(LeafLength);

    private static int LeafLength(WpfInline leaf) => leaf switch
    {
        Run run => run.Text.Length,
        LineBreak => 1,
        _ => FlowConverter.TokenOf(leaf)?.ToMarkup().Length ?? 0,
    };

    public static IEnumerable<WpfInline> Leaves(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Span span)
            {
                foreach (var child in Leaves(span.Inlines))
                    yield return child;
            }
            else
            {
                yield return inline;
            }
        }
    }

    // ---- Pixels ----

    /// <summary>The element as drawn, without its offset in the window (a VisualBrush ignores it).</summary>
    public static BitmapSource Rasterize(Visual element, int width, int height)
    {
        var visual = NewVisual();
        using (var dc = visual.RenderOpen())
        {
            var brush = new VisualBrush(element)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, width, height),
            };
            dc.DrawRectangle(brush, null, new Rect(0, 0, width, height));
        }
        return Rasterize(visual, width, height);
    }

    public static BitmapSource Rasterize(DrawingVisual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static DrawingVisual NewVisual()
    {
        var visual = new DrawingVisual();
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale);
        return visual;
    }

    /// <summary>
    /// The share of ink pixels (drawn in either image) that differ noticeably. Text only, on
    /// transparent, so the background can't dilute the figure.
    /// </summary>
    public static double DifferingInkPercent(BitmapSource a, BitmapSource b)
    {
        var w = a.PixelWidth;
        var h = a.PixelHeight;
        var pa = new byte[w * h * 4];
        var pb = new byte[w * h * 4];
        a.CopyPixels(pa, w * 4, 0);
        b.CopyPixels(pb, w * 4, 0);
        int ink = 0, differ = 0;
        for (var i = 0; i < pa.Length; i += 4)
        {
            if (pa[i + 3] < 32 && pb[i + 3] < 32)
                continue;
            ink++;
            var d = 0;
            for (var c = 0; c < 4; c++)
                d = Math.Max(d, Math.Abs(pa[i + c] - pb[i + c]));
            if (d > 64)
                differ++;
        }
        return ink == 0 ? 0 : 100.0 * differ / ink;
    }
}
