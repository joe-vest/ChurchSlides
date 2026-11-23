using System.Windows.Documents;
using ChurchSlides.Domain.Text;

namespace ChurchSlides.Spike;

/// <summary>Keeps tagged-run tokens (C0.3 decision 2, design b) whole while editing.</summary>
public static class TokenChips
{
    /// <summary>The tagged-run token a position is inside or touching, if any.</summary>
    public static Run? TaggedRunAt(TextPointer position) => position.Parent is Run { Tag: Token } run ? run : null;

    /// <summary>
    /// Widens a range so it never covers part of a token: formatting applies to the whole token
    /// (VAR-006), since a token has one format in the model.
    /// </summary>
    public static (TextPointer Start, TextPointer End) ExpandToTokens(TextRange range)
    {
        var start = range.Start;
        var end = range.End;
        if (TaggedRunAt(start) is { } first)
            start = first.ContentStart;
        if (TaggedRunAt(end) is { } last)
            end = last.ContentEnd;
        return (start, end);
    }

    /// <summary>Puts the chip shading on tagged runs, or takes it off (to compare with the renderer).</summary>
    public static void Shade(FlowDocument doc, bool on)
    {
        foreach (var paragraph in FlowConverter.AllParagraphs(doc.Blocks))
        {
            foreach (var run in EditorText.Leaves(paragraph.Inlines).OfType<Run>().Where(r => r.Tag is Token))
                run.Background = on ? FlowConverter.NewChipShade() : null;
        }
    }
}
