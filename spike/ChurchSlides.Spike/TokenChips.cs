using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using ChurchSlides.Domain.Resolution;
using ChurchSlides.Domain.Text;

namespace ChurchSlides.Spike;

/// <summary>
/// Token chips in the editor (C0.3 decision 2): the insert menu's tokens, and the guards that
/// keep a tagged-run token (design b) whole. Container chips (design a) are atomic in the control
/// already; they only need the formatting fix in RichTextPanel.ApplyToSelection.
/// </summary>
public static class TokenChips
{
    /// <summary>The insert menu: one of each kind (SLD-027, CCLI-003).</summary>
    public static readonly IReadOnlyList<(string Label, string Markup)> Menu =
    [
        ("Variable: preacher", "{{preacher}}"),
        ("Variable: sermonTitle", "{{sermonTitle}}"),
        ("Timer: ServiceStart (empty box only)", "{{timer:ServiceStart}}"),
        ("Show title", "{{show.title}}"),
        ("Show authors", "{{show.authors}}"),
        ("Show copyright", "{{show.copyright}}"),
        ("Show CCLI number", "{{show.ccli}}"),
        ("Church CCLI licence", "{{church.ccliLicense}}"),
    ];

    public static readonly DependencyProperty[] FormattingProperties =
    [
        TextElement.FontFamilyProperty,
        TextElement.FontSizeProperty,
        TextElement.ForegroundProperty,
        TextElement.FontWeightProperty,
        TextElement.FontStyleProperty,
    ];

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
        foreach (var run in TaggedRuns(doc))
            run.Background = on ? FlowConverter.NewChipShade() : null;
    }

    /// <summary>True if a Timer token may go in a box with this content: only an empty one (TMR-009).</summary>
    public static bool TimerAllowedIn(RichText text) => string.IsNullOrWhiteSpace(text.ToPlainText());

    /// <summary>Keeps tagged-run tokens whole in an editor: caret, typing, and repair after edits.</summary>
    public static void Guard(RichTextBox editor)
    {
        TextPointer? lastCaret = null;
        var busy = false;

        // The caret never rests inside a token: it jumps to the edge in the direction it moved.
        editor.SelectionChanged += (_, _) =>
        {
            if (busy || !editor.Selection.IsEmpty)
                return;
            var caret = editor.CaretPosition;
            if (TaggedRunAt(caret) is { } run && caret.CompareTo(run.ContentStart) > 0 && caret.CompareTo(run.ContentEnd) < 0)
            {
                var forward = lastCaret is null || caret.CompareTo(lastCaret) > 0;
                busy = true;
                editor.CaretPosition = forward ? run.ContentEnd : run.ContentStart;
                busy = false;
            }
            lastCaret = editor.CaretPosition;
        };

        // Typing at a token's edge goes into a new run beside it, not into the token.
        editor.PreviewTextInput += (_, e) =>
        {
            if (!editor.Selection.IsEmpty || TaggedRunAt(editor.CaretPosition) is not { } run)
                return;
            var before = editor.CaretPosition.CompareTo(run.ContentStart) == 0;
            var typed = new Run(e.Text, before ? run.ElementStart : run.ElementEnd);
            CopyFormat(run, typed);
            editor.CaretPosition = typed.ContentEnd;
            e.Handled = true;
        };

        editor.TextChanged += (_, _) =>
        {
            if (busy)
                return;
            busy = true;
            try
            {
                Repair(editor.Document);
            }
            finally
            {
                busy = false;
            }
        };
    }

    /// <summary>
    /// A tagged run whose text isn't its markup any more: partly deleted, it goes whole; with its
    /// markup still in it (text typed or pasted in, or the control merged two tokens into one
    /// run), it is read again as tokens and text.
    /// </summary>
    public static void Repair(FlowDocument doc)
    {
        foreach (var run in TaggedRuns(doc).ToList())
        {
            var markup = ((Token)run.Tag).ToMarkup();
            if (run.Text == markup)
                continue;
            if (run.Text.Contains(markup, StringComparison.Ordinal))
            {
                foreach (var piece in TokenMarkup.Parse(run.Text))
                {
                    var replacement = piece is TokenRun token
                        ? (Run)FlowConverter.NewChip(token.Token, ChipDesign.TaggedRun)
                        : new Run(((TextRun)piece).Text);
                    CopyFormat(run, replacement);
                    run.SiblingInlines.InsertBefore(run, replacement);
                }
            }
            run.SiblingInlines.Remove(run);
        }
    }

    /// <summary>Copies the five formatting properties set on one element to another.</summary>
    public static void CopyFormat(TextElement from, TextElement to)
    {
        foreach (var property in FormattingProperties)
        {
            var value = from.ReadLocalValue(property);
            if (value != DependencyProperty.UnsetValue)
                to.SetValue(property, value);
        }
    }

    private static IEnumerable<Run> TaggedRuns(FlowDocument doc) =>
        FlowConverter.AllParagraphs(doc.Blocks).SelectMany(p => EditorText.Leaves(p.Inlines)).OfType<Run>().Where(r => r.Tag is Token);
}
