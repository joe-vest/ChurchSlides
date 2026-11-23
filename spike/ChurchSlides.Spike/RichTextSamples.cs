using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Resolution;
using ChurchSlides.Domain.Text;
using Inline = ChurchSlides.Domain.Text.Inline;
using Paragraph = ChurchSlides.Domain.Text.Paragraph;

namespace ChurchSlides.Spike;

public sealed record RichTextSample(string Name, RichText Text, BoxStyle Style)
{
    public override string ToString() => Name;
}

/// <summary>Fixed samples for S3 (C0.3 plan) and seeded random documents for the round trip.</summary>
public static class RichTextSamples
{
    private static readonly TextDefaults Hymn = new() { FontFamily = "Segoe UI", FontSizeRelative = 0.065, ColorHex = "#FFFFFF" };

    public static readonly IReadOnlyList<RichTextSample> All =
    [
        Sample("Hymn verse, one bold word", Hymn,
            P(T("Amazing grace! How "), T("sweet", new RunFormat { Bold = true }), T(" the sound")),
            P(T("That saved a wretch like me!")),
            P(T("I once was lost, but now am found;")),
            P(T("Was blind, but now I see."))),

        Sample("Mixed fonts and sizes in a line", Hymn with { HAlign = HorizontalAlign.Left },
            P(T("Be "), T("Thou", new RunFormat { FontFamily = "Georgia", FontSize = 0.09 }), T(" my "),
              T("Vision", new RunFormat { FontFamily = "Times New Roman", FontSize = 0.05, Italic = true }),
              T(", O Lord of my "), T("heart", new RunFormat { FontFamily = "Consolas", FontSize = 0.075, Bold = true })),
            P(T("Naught be all else to me, save that Thou art."))),

        Sample("Colored range across a line break", Hymn with { LineSpacing = 1.2 },
            P(T("When peace like a river "), T("attendeth my way,", new RunFormat { ColorHex = "#FFD54F" })),
            P(T("When sorrows like sea billows", new RunFormat { ColorHex = "#FFD54F" }), T(" roll;")),
            P(T("Whatever my lot, Thou hast taught me to say,"))),

        Sample("Italic chorus, explicit and default runs", Hymn with { Italic = true, VAlign = VerticalAlign.Bottom },
            P(T("It is well "), T("(explicit italic)", new RunFormat { Italic = true }), T(" with my soul,")),
            P(T("It is well, "), T("it is well", new RunFormat { Italic = false, Bold = true }), T(" with my soul."))),

        Sample("Long text that overflows", Hymn with { FontSizeRelative = 0.075, VAlign = VerticalAlign.Top },
            [.. TestContent.ItIsWell.Take(3).SelectMany(v => v.Split('\n')).Select(line => P(T(line)))]),

        Sample("Each token kind, formatted (VAR-006)", Hymn with { FontSizeRelative = 0.055 },
            P(T("Preacher: "), Tok("{{preacher}}", new RunFormat { Bold = true, ColorHex = "#90CAF9" })),
            P(T("Today: "), Tok("{{sermonTitle}}", new RunFormat { Italic = true })),
            P(Tok("{{show.title}}", new RunFormat { FontFamily = "Georgia" }), T(" by "), Tok("{{show.authors}}")),
            P(Tok("{{show.copyright}}", new RunFormat { FontSize = 0.035 }), T(" · CCLI song "), Tok("{{show.ccli}}")),
            P(Tok("{{church.ccliLicense}}", new RunFormat { FontSize = 0.035, ColorHex = "#B0BEC5" }))),

        Sample("Timer token alone (TMR-009)", Hymn with { FontSizeRelative = 0.18, Bold = true },
            P(Tok("{{timer:ServiceStart}}"))),

        Sample("Timer token next to text (TMR-009 problem)", Hymn,
            P(T("Service starts in "), Tok("{{timer:ServiceStart}}", new RunFormat { Bold = true }))),

        Sample("Font that isn't installed (fallback)", Hymn with { FontFamily = "Hymnal Sans Not Installed" },
            P(T("Holy, holy, holy! Lord God Almighty!")),
            P(T("Early in the morning "), T("our song", new RunFormat { FontFamily = "Another Missing Font", Bold = true }), T(" shall rise to Thee."))),
    ];

    /// <summary>True when every Timer token stands alone in its box (TMR-009).</summary>
    public static bool TimersStandAlone(RichText text)
    {
        var inlines = text.Paragraphs.SelectMany(p => p.Inlines).Where(i => i is not TextRun { Text: var t } || !string.IsNullOrWhiteSpace(t)).ToList();
        return !inlines.Any(i => i is TokenRun { Token: TimerToken }) || inlines.Count == 1;
    }

    // ---- Random documents (S3-a) ----

    private static readonly string[] Words =
        ["grace", "sweet", "sound", "Lord", "peace", "river", "soul", "holy", "morning", "song", "light", "  ", "!", ",", "é", "ü", "–", "'"];

    private static readonly string[] Families = ["Segoe UI", "Georgia", "Arial", "Times New Roman", "Consolas"];
    private static readonly double[] Sizes = [0.04, 0.05, 0.065, 0.08, 0.1];
    private static readonly string[] Colors = ["#FFFFFF", "#FFD54F", "#90CAF9", "#EF9A9A", "#000000"];
    private static readonly string[] Tokens = ["{{preacher}}", "{{timer:Countdown}}", "{{show.title}}", "{{show.ccli}}", "{{church.ccliLicense}}"];

    /// <summary>A random document in the model's normal form (merged runs), so a lossless round trip returns it unchanged.</summary>
    public static RichText RandomDocument(int seed)
    {
        var random = new Random(seed);
        var paragraphs = new List<Paragraph>();
        var count = random.Next(1, 5);
        for (var p = 0; p < count; p++)
        {
            var inlines = new List<Inline>();
            var runs = random.Next(count == 1 ? 1 : 0, 6);
            for (var r = 0; r < runs; r++)
            {
                var format = RandomFormat(random);
                if (random.NextDouble() < 0.15)
                {
                    inlines.Add(Tok(Tokens[random.Next(Tokens.Length)], format));
                }
                else
                {
                    var words = Enumerable.Range(0, random.Next(1, 5)).Select(_ => Words[random.Next(Words.Length)]);
                    inlines.Add(T(string.Join(" ", words), format));
                }
            }
            paragraphs.Add(new Paragraph { Inlines = [.. inlines] });
        }
        return FlowConverter.Normalize(new RichText { Paragraphs = [.. paragraphs] });
    }

    /// <summary>
    /// Two to five real hymn lines with random formatting on whole words: closer to what the
    /// church types than <see cref="RandomDocument"/>, for the line-break comparison.
    /// </summary>
    public static RichText RandomLyric(int seed)
    {
        var random = new Random(seed);
        var lines = TestContent.AmazingGrace.Concat(TestContent.ItIsWell).SelectMany(v => v.Split('\n')).Distinct().ToList();
        var paragraphs = new List<Paragraph>();
        for (var p = random.Next(2, 6); p > 0; p--)
        {
            var words = lines[random.Next(lines.Count)].Split(' ');
            var inlines = new List<Inline>();
            for (var w = 0; w < words.Length; w++)
            {
                var format = random.NextDouble() < 0.75 ? null : RandomFormat(random);
                inlines.Add(T(words[w], format));
                if (w < words.Length - 1)
                    inlines.Add(T(" ", format));
            }
            paragraphs.Add(new Paragraph { Inlines = [.. inlines] });
        }
        return FlowConverter.Normalize(new RichText { Paragraphs = [.. paragraphs] });
    }

    private static RunFormat? RandomFormat(Random random)
    {
        if (random.NextDouble() < 0.4)
            return null;
        var f = new RunFormat
        {
            FontFamily = random.NextDouble() < 0.3 ? Families[random.Next(Families.Length)] : null,
            FontSize = random.NextDouble() < 0.3 ? Sizes[random.Next(Sizes.Length)] : null,
            ColorHex = random.NextDouble() < 0.3 ? Colors[random.Next(Colors.Length)] : null,
            Bold = random.NextDouble() < 0.3 ? random.NextDouble() < 0.7 : null,
            Italic = random.NextDouble() < 0.3 ? random.NextDouble() < 0.7 : null,
        };
        return f == RunFormat.None ? null : f;
    }

    /// <summary>About 200 words of hymn text in one box, for the keystroke timing.</summary>
    public static RichTextSample TwoHundredWords()
    {
        var lines = TestContent.AmazingGrace.Concat(TestContent.ItIsWell).SelectMany(v => v.Split('\n')).ToList();
        var paragraphs = new List<Paragraph>();
        var words = 0;
        foreach (var line in lines)
        {
            if (words >= 200)
                break;
            paragraphs.Add(P(T(line)));
            words += line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        }
        return new RichTextSample("200 words", new RichText { Paragraphs = [.. paragraphs] },
            new BoxStyle(Hymn with { FontSizeRelative = 0.03, HAlign = HorizontalAlign.Left, VAlign = VerticalAlign.Top }, BoxStyle.StandardGeometry));
    }

    // ---- Builders ----

    private static RichTextSample Sample(string name, TextDefaults defaults, params Paragraph[] paragraphs) =>
        new(name, new RichText { Paragraphs = [.. paragraphs] }, new BoxStyle(defaults, BoxStyle.StandardGeometry));

    public static Paragraph P(params Inline[] inlines) => new() { Inlines = [.. inlines] };

    public static TextRun T(string text, RunFormat? format = null) => new() { Text = text, Format = format };

    public static TokenRun Tok(string markup, RunFormat? format = null) =>
        TokenMarkup.Parse(markup) is [TokenRun token] ? token with { Format = format } : throw new ArgumentException($"Not a token: {markup}");
}
