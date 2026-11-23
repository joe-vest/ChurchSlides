using ChurchSlides.Domain.Text;
using Inline = ChurchSlides.Domain.Text.Inline;
using Paragraph = ChurchSlides.Domain.Text.Paragraph;

namespace ChurchSlides.Spike;

/// <summary>
/// Edits on the model by plain-text offset (paragraphs joined by '\n', tokens as markup): the
/// spike's copy, paste and token insertion go through these and reload the editor, so tokens and
/// formatting can't be mangled by the control's own clipboard handling.
/// </summary>
public static class ModelEdit
{
    public static int Length(RichText text) => text.ToPlainText().Length;

    /// <summary>The part of the model between two offsets. A token partly inside is taken whole.</summary>
    public static RichText Slice(RichText text, int start, int end)
    {
        var paragraphs = new List<Paragraph>();
        var position = 0;
        foreach (var paragraph in Paragraphs(text))
        {
            var length = paragraph.Inlines.Sum(InlineLength);
            if (position <= end && position + length >= start)
            {
                var inlines = new List<Inline>();
                var at = position;
                foreach (var inline in paragraph.Inlines)
                {
                    var from = Math.Max(start, at) - at;
                    var to = Math.Min(end, at + InlineLength(inline)) - at;
                    if (inline is TextRun run && to > from)
                        inlines.Add(run with { Text = run.Text[from..to] });
                    else if (inline is TokenRun && to > from)
                        inlines.Add(inline);
                    at += InlineLength(inline);
                }
                paragraphs.Add(new Paragraph { Inlines = [.. inlines] });
            }
            position += length + 1;
        }
        return new RichText { Paragraphs = [.. paragraphs] };
    }

    /// <summary>Replaces the text between two offsets with a fragment.</summary>
    public static RichText Splice(RichText text, int start, int end, RichText fragment) =>
        FlowConverter.Normalize(Concat(Concat(Slice(text, 0, start), fragment), Slice(text, end, Length(text))));

    /// <summary>The second document continues the first one's last paragraph.</summary>
    private static RichText Concat(RichText a, RichText b)
    {
        var first = Paragraphs(a).ToList();
        var second = Paragraphs(b).ToList();
        var joined = first[^1] with { Inlines = [.. first[^1].Inlines, .. second[0].Inlines] };
        return new RichText { Paragraphs = [.. first[..^1], joined, .. second[1..]] };
    }

    /// <summary>Empty has no paragraphs; for offsets it is one empty paragraph.</summary>
    private static IEnumerable<Paragraph> Paragraphs(RichText text) =>
        text.Paragraphs.Length == 0 ? [new Paragraph()] : text.Paragraphs;

    private static int InlineLength(Inline inline) => inline switch
    {
        TextRun run => run.Text.Length,
        TokenRun token => token.Token.ToMarkup().Length,
        _ => 0,
    };
}
