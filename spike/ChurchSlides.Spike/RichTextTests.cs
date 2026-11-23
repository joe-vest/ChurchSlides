using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Text;
using static ChurchSlides.Spike.RichTextSamples;

namespace ChurchSlides.Spike;

/// <summary>
/// The S3 automatic run (C0.3 plan): round trips, editor against renderer, scripted edits and
/// timings. Drives the tab's own editor, so it runs on the UI thread with the tab visible.
/// </summary>
public sealed class RichTextTests(RichTextPanel panel, Action<string> status)
{
    private const int RandomDocuments = 500;

    /// <summary>The church's output size, for the per-slide renderer timing.</summary>
    private const int OutputWidth = 1440;

    private const int OutputHeight = 900;

    private static readonly ChipDesign[] Designs = [ChipDesign.TaggedRun, ChipDesign.Container];

    private readonly List<Figure> figures = [];

    public async Task<List<Figure>> Run()
    {
        var chips = panel.Chips;
        try
        {
            foreach (var design in Designs)
            {
                status($"S3: round trips ({Name(design)})…");
                await RoundTrips(design);
            }
            status("S3: editor against renderer…");
            await Compare();
            await WidthProbe();
            status("S3: scripted edits…");
            foreach (var design in Designs)
                ScriptedEdits(design);
            status("S3: timings…");
            await Timings();
            Add("TMR-009 check: lone Timer / Timer next to text",
                $"{Verdict(TimersStandAlone(All.First(s => s.Name.StartsWith("Timer token alone", StringComparison.Ordinal)).Text))} / " +
                $"{Verdict(TimersStandAlone(All.First(s => s.Name.StartsWith("Timer token next", StringComparison.Ordinal)).Text))} (expected: alone ok / flagged)");
        }
        finally
        {
            panel.Chips = chips;
            panel.Load(All[0].Text, All[0].Style);
        }
        return figures;
    }

    private static string Verdict(bool alone) => alone ? "ok" : "flagged";

    // ---- S3-a: round trips ----

    private async Task RoundTrips(ChipDesign design)
    {
        // Samples through the visible editor.
        panel.Chips = design;
        var failures = new List<string>();
        foreach (var sample in All)
        {
            panel.Load(sample.Text, sample.Style);
            if (Difference(FlowConverter.Normalize(sample.Text), panel.Model) is { } diff)
                failures.Add($"{sample.Name}: {diff}");
        }
        Add($"Round trip, samples ({Name(design)})", failures.Count == 0
            ? $"all {All.Count} unchanged"
            : $"{All.Count - failures.Count} of {All.Count} unchanged. {string.Join(" | ", failures)}");

        // Random documents through an editor that isn't on screen (faster; same document handling).
        var offscreen = new RichTextBox();
        var style = All[0].Style;
        string? firstFailure = null;
        var same = 0;
        for (var seed = 1; seed <= RandomDocuments; seed++)
        {
            var text = RandomDocument(seed);
            offscreen.Document = FlowConverter.ToDocument(text, style, RichTextPanel.CanvasHeight, design);
            if (Difference(text, FlowConverter.ToModel(offscreen.Document, RichTextPanel.CanvasHeight)) is { } diff)
                firstFailure ??= $"seed {seed}: {diff}";
            else
                same++;
            if (seed % 100 == 0)
                await Yield();
        }
        Add($"Round trip, {RandomDocuments} random documents ({Name(design)})",
            firstFailure is null ? $"all {RandomDocuments} unchanged" : $"{same} of {RandomDocuments} unchanged. First: {firstFailure}");

        // The copy-and-paste path inside the editor: the document saved and loaded as XAML.
        var xamlSame = 0;
        int becameExplicit = 0, tokensLost = 0;
        string? xamlError = null;
        foreach (var sample in All)
        {
            try
            {
                var back = ThroughXaml(sample.Text, sample.Style, design);
                var expected = FlowConverter.Normalize(sample.Text);
                if (Difference(expected, back) is null)
                    xamlSame++;
                becameExplicit += Inlines(back).Count(i => i.Format is not null) - Inlines(expected).Count(i => i.Format is not null);
                tokensLost += Inlines(expected).Count(i => i is TokenRun) - Inlines(back).Count(i => i is TokenRun);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.Xml.XmlException or System.Windows.Markup.XamlParseException)
            {
                xamlError ??= $"{sample.Name}: {ex.GetType().Name}: {ex.Message}";
            }
        }
        Add($"Copy/paste format (XAML) round trip ({Name(design)})",
            $"{xamlSame} of {All.Count} unchanged; net {becameExplicit} more runs with explicit formatting; {tokensLost} tokens lost" +
            (xamlError is null ? "" : $"; error: {xamlError}"));
    }

    private static RichText ThroughXaml(RichText text, BoxStyle style, ChipDesign design)
    {
        var source = FlowConverter.ToDocument(text, style, RichTextPanel.CanvasHeight, design);
        using var stream = new MemoryStream();
        new TextRange(source.ContentStart, source.ContentEnd).Save(stream, DataFormats.Xaml);
        stream.Position = 0;
        var target = FlowConverter.ToDocument(RichText.Empty, style, RichTextPanel.CanvasHeight, design);
        new TextRange(target.ContentStart, target.ContentEnd).Load(stream, DataFormats.Xaml);
        return FlowConverter.ToModel(target, RichTextPanel.CanvasHeight);
    }

    private static IEnumerable<ChurchSlides.Domain.Text.Inline> Inlines(RichText text) => text.Paragraphs.SelectMany(p => p.Inlines);

    // ---- S3-b: editor against renderer ----

    private async Task Compare()
    {
        foreach (var design in Designs)
        {
            panel.Chips = design;
            foreach (var sample in All)
            {
                if (design == ChipDesign.Container && !Inlines(sample.Text).Any(i => i is TokenRun))
                    continue; // without tokens both designs are the same document
                panel.Load(sample.Text, sample.Style);
                panel.Editor.ScrollToHome();
                await Yield();

                TokenChips.Shade(panel.Editor.Document, false); // the renderer has no chip shading
                var editorBitmap = panel.RasterizeEditor();
                TokenChips.Shade(panel.Editor.Document, true);
                var rendererBitmap = panel.RenderBox(sample.Text, RichTextRenderer.Markup, out var layout);
                var editorStarts = EditorText.LineStarts(panel.Editor);
                var rendererStarts = RichTextRenderer.LineStarts(layout);
                var editorHeight = panel.Editor.ExtentHeight - panel.Editor.Document.PagePadding.Top;
                var editorOverflows = editorHeight > panel.BoxSize.Height + 0.5;
                var breaks = editorStarts.SequenceEqual(rendererStarts)
                    ? "breaks same"
                    : $"breaks differ (editor {string.Join(",", editorStarts)}; renderer {string.Join(",", rendererStarts)})";
                Dump($"{sample.Name} ({Name(design)})", editorBitmap, rendererBitmap,
                    Invariant($"editor viewport {panel.Editor.ViewportWidth:F2} px, renderer text width {layout.Text.Width:F2} px"));
                Add($"Editor / renderer: {sample.Name} ({Name(design)})", Invariant(
                    $"lines {editorStarts.Count} / {rendererStarts.Count}, {breaks}, {EditorText.DifferingInkPercent(editorBitmap, rendererBitmap):F1}% ink pixels differ, text height {editorHeight:F1} / {layout.Text.Height:F1} px, overflow {YesNo(editorOverflows)} / {YesNo(layout.Overflows)}"));
            }
        }
    }

    /// <summary>For development: with SPIKE_S3_DUMP set to a folder, saves each comparison's two images there.</summary>
    private static void Dump(string name, BitmapSource editor, BitmapSource renderer, string note)
    {
        if (Environment.GetEnvironmentVariable("SPIKE_S3_DUMP") is not { Length: > 0 } folder)
            return;
        Directory.CreateDirectory(folder);
        var safe = string.Concat(name.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        foreach (var (suffix, bitmap) in new[] { ("editor", editor), ("renderer", renderer) })
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(System.IO.Path.Combine(folder, $"{safe}-{suffix}.png"));
            encoder.Save(file);
        }
        // Overlay: editor ink red, renderer ink green, both yellow.
        int w = editor.PixelWidth, h = editor.PixelHeight;
        var a = new byte[w * h * 4];
        var b = new byte[w * h * 4];
        editor.CopyPixels(a, w * 4, 0);
        renderer.CopyPixels(b, w * 4, 0);
        var o = new byte[w * h * 4];
        for (var i = 0; i < o.Length; i += 4)
        {
            o[i + 2] = a[i + 3];
            o[i + 1] = b[i + 3];
            o[i + 3] = 255;
        }
        var overlay = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, o, w * 4);
        var overlayEncoder = new PngBitmapEncoder();
        overlayEncoder.Frames.Add(BitmapFrame.Create(overlay));
        using (var file = File.Create(System.IO.Path.Combine(folder, $"{safe}-overlay.png")))
            overlayEncoder.Save(file);
        Note($"{name}: {note}");
    }

    /// <summary>
    /// How much wider the editor must be to break lines as the renderer does, on the long sample
    /// (the editor wraps a little early). Tries 0 to 24 px.
    /// </summary>
    private async Task WidthProbe()
    {
        panel.Chips = ChipDesign.TaggedRun;
        var sample = All.First(s => s.Name.StartsWith("Long text", StringComparison.Ordinal));
        panel.Load(sample.Text, sample.Style);
        panel.RenderBox(sample.Text, RichTextRenderer.Markup, out var layout);
        var wanted = RichTextRenderer.LineStarts(layout);
        int? found = null;
        for (var extra = 0; extra <= 24 && found is null; extra++)
        {
            panel.Editor.Width = panel.BoxSize.Width + extra;
            panel.Editor.UpdateLayout();
            if (EditorText.LineStarts(panel.Editor).SequenceEqual(wanted))
                found = extra;
        }
        Add("Extra editor width for the renderer's line breaks (long sample)", found is { } px ? $"{px} px" : "more than 24 px");

        // The same comparison on random documents, as they are and with that extra width.
        var style = sample.Style with { Defaults = sample.Style.Defaults with { VAlign = ChurchSlides.Domain.Model.VerticalAlign.Top } };
        foreach (var (kind, make) in new (string, Func<int, RichText>)[] { ("hymn lines, words formatted", RandomLyric), ("random words and punctuation", RandomDocument) })
        {
            var counts = new List<string>();
            foreach (var extra in new[] { 0, found ?? 0 }.Distinct())
            {
                panel.Editor.Width = panel.BoxSize.Width + extra;
                var differ = 0;
                for (var seed = 1; seed <= 100; seed++)
                {
                    var text = make(seed);
                    panel.Load(text, style);
                    panel.RenderBox(text, RichTextRenderer.Markup, out var randomLayout);
                    var editorStarts = EditorText.LineStarts(panel.Editor);
                    var rendererStarts = RichTextRenderer.LineStarts(randomLayout);
                    if (!editorStarts.SequenceEqual(rendererStarts))
                    {
                        differ++;
                        Note($"{kind} seed {seed} +{extra}: editor {string.Join(",", editorStarts)} renderer {string.Join(",", rendererStarts)} text {Describe(text)}");
                    }
                    if (seed % 20 == 0)
                        await Yield();
                }
                counts.Add($"{differ} at +{extra} px");
            }
            panel.Editor.Width = double.NaN;
            Add($"Line breaks differ, 100 random documents ({kind})", string.Join(", ", counts));
        }
    }

    /// <summary>For development: a line in the dump folder's notes (SPIKE_S3_DUMP).</summary>
    private static void Note(string line)
    {
        if (Environment.GetEnvironmentVariable("SPIKE_S3_DUMP") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            File.AppendAllText(System.IO.Path.Combine(folder, "notes.txt"), line + Environment.NewLine);
        }
    }

    private static string YesNo(bool value) => value ? "yes" : "no";

    // ---- S3-a, S3-e: scripted edits through the control ----

    private void ScriptedEdits(ChipDesign design)
    {
        panel.Chips = design;
        var style = All[0].Style;
        var bold = new RunFormat { Bold = true };
        var italic = new RunFormat { Italic = true };

        // "Amazing " 0-8, "grace" 8-13, " how sweet" 13-23. Bold 4..16.
        Edit(design, "bold a range across runs", style,
            Doc(P(T("Amazing "), T("grace", italic), T(" how sweet"))),
            () => ApplyRange(4, 16, TextElement.FontWeightProperty, FontWeights.Bold),
            Doc(P(T("Amaz"), T("ing ", bold), T("grace", new RunFormat { Bold = true, Italic = true }), T(" ho", bold), T("w sweet"))));

        // "Preacher: " 0-10, token 10-22. Recolor 12..16, which is inside the token.
        Edit(design, "recolor part of a token", style,
            Doc(P(T("Preacher: "), Tok("{{preacher}}"))),
            () => ApplyRange(12, 16, TextElement.ForegroundProperty, FlowConverter.BrushOf("#FFD54F")),
            Doc(P(T("Preacher: "), Tok("{{preacher}}", new RunFormat { ColorHex = "#FFD54F" }))));

        Edit(design, "type inside a bold run", style,
            Doc(P(T("Amazing "), T("grace", bold), T(" sound"))),
            () =>
            {
                panel.Editor.CaretPosition = EditorText.PointerAt(panel.Editor.Document, 10);
                Type(panel.Editor, "xyz");
            },
            Doc(P(T("Amazing "), T("grxyzace", bold), T(" sound"))));

        // "first line" 0-10, \n, "second" 11-17, " line" 17-22. Delete 6..14.
        Edit(design, "delete across paragraphs", style,
            Doc(P(T("first line")), P(T("second", bold), T(" line")), P(T("third line"))),
            () =>
            {
                var doc = panel.Editor.Document;
                panel.Editor.Selection.Select(EditorText.PointerAt(doc, 6), EditorText.PointerAt(doc, 14));
                EditingCommands.Delete.Execute(null, panel.Editor);
            },
            Doc(P(T("first "), T("ond", bold), T(" line")), P(T("third line"))));

        Edit(design, "bold two neighbouring tokens", style,
            Doc(P(Tok("{{show.title}}"), Tok("{{show.ccli}}"), T(" end"))),
            () => ApplyRange(0, 26, TextElement.FontWeightProperty, FontWeights.Bold),
            Doc(P(Tok("{{show.title}}", bold), Tok("{{show.ccli}}", bold), T(" end"))));

        var tokens = All.First(s => s.Name.StartsWith("Each token kind", StringComparison.Ordinal));
        Edit(design, "Clear formatting on everything (TPL-025)", tokens.Style,
            tokens.Text,
            () =>
            {
                panel.Editor.SelectAll();
                panel.ClearFormatting();
            },
            FlowConverter.Normalize(tokens.Text.WithoutFormatting()));
    }

    private void ApplyRange(int start, int end, DependencyProperty property, object value)
    {
        var doc = panel.Editor.Document;
        panel.Editor.Selection.Select(EditorText.PointerAt(doc, start), EditorText.PointerAt(doc, end));
        panel.ApplyToSelection(property, value);
    }

    private void Edit(ChipDesign design, string name, BoxStyle style, RichText start, Action edit, RichText expected)
    {
        panel.Load(start, style);
        string result;
        try
        {
            edit();
            result = Difference(FlowConverter.Normalize(expected), panel.Model) is { } diff ? $"DIFFERENT: {diff}" : "as expected";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            result = $"failed: {ex.GetType().Name}: {ex.Message}";
        }
        Add($"Edit: {name} ({Name(design)})", result);
    }

    /// <summary>
    /// Types as the keyboard would: a text composition through the input manager. The control
    /// queues typed text and inserts it at Background priority, so this waits for that.
    /// Returns false if the text didn't arrive.
    /// </summary>
    public static bool Type(RichTextBox editor, string text)
    {
        var before = TextLength(editor);
        TypeQueued(editor, text);
        editor.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        return TextLength(editor) == before + text.Length;
    }

    /// <summary>Starts typing without waiting for the control to insert it (as a real key press).</summary>
    public static void TypeQueued(RichTextBox editor, string text)
    {
        editor.Focus();
        TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, editor, text));
    }

    public static int TextLength(RichTextBox editor) => new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text.Length;

    // ---- S3-d: timings ----

    private async Task Timings()
    {
        panel.Chips = ChipDesign.TaggedRun;
        var toDocument = new List<double>();
        var toModel = new List<double>();
        var load = new List<double>();
        var plain = new List<double>();
        var outlined = new List<double>();
        var outputBox = new Size(BoxStyle.StandardGeometry.Width * OutputWidth, BoxStyle.StandardGeometry.Height * OutputHeight);
        var offset = new Vector(BoxStyle.StandardGeometry.X * OutputWidth, BoxStyle.StandardGeometry.Y * OutputHeight);

        foreach (var sample in All)
        {
            for (var i = 0; i < 5; i++)
            {
                var clock = Stopwatch.StartNew();
                var doc = FlowConverter.ToDocument(sample.Text, sample.Style, RichTextPanel.CanvasHeight, ChipDesign.TaggedRun);
                toDocument.Add(clock.Elapsed.TotalMilliseconds);

                clock.Restart();
                panel.Editor.Document = doc;
                panel.Editor.UpdateLayout();
                load.Add(clock.Elapsed.TotalMilliseconds);

                clock.Restart();
                _ = FlowConverter.ToModel(panel.Editor.Document, RichTextPanel.CanvasHeight);
                toModel.Add(clock.Elapsed.TotalMilliseconds);

                plain.Add(RenderSlide(sample, outputBox, offset, 0));
                outlined.Add(RenderSlide(sample, outputBox, offset, 0.004 * OutputHeight));
            }
            await Yield();
        }
        Add("Model → document", Summary.Of(toDocument).ToString());
        Add("Document load into the editor (with layout)", Summary.Of(load).ToString());
        Add("Document → model", Summary.Of(toModel).ToString());
        Add($"Renderer per slide, {OutputWidth}×{OutputHeight} text layer", Summary.Of(plain).ToString());
        Add($"Renderer per slide with geometry outline (SLD-025)", Summary.Of(outlined).ToString());

        await Keystrokes();
    }

    /// <summary>Lays out, draws and rasterizes one Text Box's layer at output size. Milliseconds.</summary>
    private static double RenderSlide(RichTextSample sample, Size box, Vector offset, double outline)
    {
        var clock = Stopwatch.StartNew();
        var layout = RichTextRenderer.Layout(sample.Text, sample.Style, box, OutputHeight, RichTextRenderer.SampleValue);
        var visual = EditorText.NewVisual();
        using (var dc = visual.RenderOpen())
            RichTextRenderer.Draw(dc, layout, offset, outline);
        _ = EditorText.Rasterize(visual, OutputWidth, OutputHeight);
        return clock.Elapsed.TotalMilliseconds;
    }

    /// <summary>
    /// Keystroke to the frame after the one showing it (an upper bound, as in S2), typing at the
    /// end of a 200-word box.
    /// </summary>
    private async Task Keystrokes()
    {
        var sample = TwoHundredWords();
        panel.Load(sample.Text, sample.Style);
        panel.Editor.Focus();
        panel.Editor.CaretPosition = panel.Editor.Document.ContentEnd;
        await Yield();

        var monitor = new FrameMonitor(60);
        var times = new List<double>();
        var before = TextLength(panel.Editor);
        monitor.Begin();
        try
        {
            for (var i = 0; i < 25; i++)
            {
                var start = monitor.Now;
                var frame = monitor.AfterFrames(2);
                TypeQueued(panel.Editor, i % 6 == 5 ? " " : "a");
                times.Add(await frame - start);
                await Task.Delay(80);
            }
        }
        finally
        {
            monitor.End();
        }
        var arrived = TextLength(panel.Editor) - before;
        Add("Keystroke to next frame (200-word box)", Summary.Of(times) + (arrived == times.Count ? "" : $"; only {arrived} of {times.Count} keystrokes arrived"));
    }

    // ---- Helpers ----

    private static RichText Doc(params ChurchSlides.Domain.Text.Paragraph[] paragraphs) => new() { Paragraphs = [.. paragraphs] };

    /// <summary>Null if the two models are the same, else both in a compact form.</summary>
    public static string? Difference(RichText expected, RichText actual) =>
        ModelJson.Of(expected) == ModelJson.Of(actual) ? null : $"expected {Describe(expected)}, got {Describe(actual)}";

    /// <summary>A model in one line: runs in quotes, tokens as markup, formats in brackets, paragraphs split by a pilcrow.</summary>
    public static string Describe(RichText text) => string.Join(" ¶ ", text.Paragraphs.Select(p => string.Concat(p.Inlines.Select(i =>
        (i switch
        {
            TextRun run => "\"" + run.Text + "\"",
            TokenRun token => token.Token.ToMarkup(),
            _ => "?",
        }) + (i.Format is { } f ? Format(f) : "")))));

    private static string Format(RunFormat f) => "[" + string.Join(",", new[]
    {
        f.Bold is { } b ? (b ? "b" : "not b") : null,
        f.Italic is { } it ? (it ? "i" : "not i") : null,
        f.ColorHex,
        f.FontFamily,
        f.FontSize?.ToString(CultureInfo.InvariantCulture),
    }.Where(x => x is not null)) + "]";

    private static string Name(ChipDesign design) => design == ChipDesign.Container ? "container chips" : "tagged-run chips";

    private void Add(string name, string value) => figures.Add(new Figure(name, value));

    private static string Invariant(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    private static async Task Yield() => await Dispatcher.Yield(DispatcherPriority.Background);
}
