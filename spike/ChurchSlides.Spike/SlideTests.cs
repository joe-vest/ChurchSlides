using System.Diagnostics;
using System.Globalization;
using System.Windows.Media.Imaging;

namespace ChurchSlides.Spike;

/// <summary>The automatic parts of S2 (slides and crossfade) and S6 (memory and decode).</summary>
public sealed class SlideTests(MonitorInfo monitor, Media media, RenderThread thread, Metrics metrics, Action<string> status)
{
    public const int FadeMs = 300;
    private const int Transitions = 60;
    private const int PeriodMs = 600;
    private const double MemoryLimitMb = 300; // PLT-005

    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    private int W => monitor.Bounds.Width;
    private int H => monitor.Bounds.Height;

    /// <summary>
    /// S2: pre-render timings with nothing else going on, then 60 transitions each of Cut,
    /// Crossfade with pre-render on the UI thread, and Crossfade with pre-render on the render thread.
    /// </summary>
    public async Task<List<Figure>> RunS2()
    {
        var slides = TestContent.HymnSlides(media.Backgrounds, FitMode.Fill);
        var figures = new List<Figure>
        {
            new("Output", $"{monitor.DeviceName}, {W}×{H} px, {monitor.RefreshHz} Hz"),
            new("Slides", $"{slides.Count} hymn slides over {media.Backgrounds.Count} backgrounds (Fill), background changes every 3 slides"),
        };

        status("S2: pre-render on the UI thread, idle…");
        var ui = await new PreRenderer(new SlideFactory(W, H), null).RenderIdle(slides);
        figures.Add(new("Pre-render idle, UI thread (this long the UI thread stalls)", PreRenderer.Timing(ui)));
        status("S2: pre-render on the render thread, idle…");
        var rt = await new PreRenderer(new SlideFactory(W, H), thread).RenderIdle(slides);
        figures.Add(new("Pre-render idle, render thread", PreRenderer.Timing(rt)));
        Collect();

        var output = new SlideOutput(monitor, media.LogoBitmap(W, H));
        if (!await output.ShowAndWait())
        {
            output.Close();
            figures.Add(new("Output window", "did not render within 5 s"));
            return figures;
        }
        try
        {
            var frames = new FrameMonitor(monitor.RefreshHz);
            foreach (var (label, fadeMs, onThread) in new[]
            {
                ("Cut", 0, true),
                ($"Crossfade {FadeMs} ms, pre-render on UI thread", FadeMs, false),
                ($"Crossfade {FadeMs} ms, pre-render on render thread", FadeMs, true),
            })
            {
                status($"S2: {label}, {Transitions} transitions…");
                var presenter = new Presenter(output, slides, new PreRenderer(new SlideFactory(W, H), onThread ? thread : null), frames);
                var from = metrics.Now;
                var results = await Step(presenter, Transitions, fadeMs, i => i % slides.Count);
                figures.AddRange(Describe(label, fadeMs, results, frames.RefreshMs, metrics.Summarize(from, metrics.Now)));
            }
        }
        finally
        {
            output.Close();
        }
        return figures;
    }

    /// <summary>
    /// S6: memory idle, with a typical Service loaded (thumbnails of every slide) and while
    /// presenting it with a 3-slide pre-render window; background decode times.
    /// </summary>
    public async Task<List<Figure>> RunS6()
    {
        var figures = new List<Figure>();
        status("S6: memory idle…");
        figures.Add(new("Memory idle (after full GC)", Describe(await Settle())));

        status("S6: decode times…");
        figures.AddRange(DecodeTimes());

        status("S6: loading the typical Service…");
        var slides = TestContent.TypicalService(media.Backgrounds);
        var clock = Stopwatch.StartNew();
        var thumbFactory = new SlideFactory(160, 100);
        var thumbnails = slides.Select(thumbFactory.RenderFlat).ToList();
        figures.Add(new("Service", $"{slides.Count} slides (3 Songs of 6, 5 Presentations of 5, Scripture of 4) over {Math.Min(media.Backgrounds.Count, 9)} backgrounds"));
        figures.Add(new("Thumbnails", string.Create(C, $"{thumbnails.Count} at 160×100 in {clock.Elapsed.TotalMilliseconds:F0} ms ({thumbnails.Count * 160 * 100 * 4 / 1048576.0:F1} MB of pixels)")));
        figures.Add(new("Memory loaded (Service and thumbnails, after full GC)", Describe(await Settle())));

        var output = new SlideOutput(monitor, media.LogoBitmap(W, H));
        if (!await output.ShowAndWait())
        {
            output.Close();
            figures.Add(new("Output window", "did not render within 5 s"));
            return figures;
        }
        try
        {
            status($"S6: presenting {slides.Count} slides…");
            var presenter = new Presenter(output, slides, new PreRenderer(new SlideFactory(W, H), thread), new FrameMonitor(monitor.RefreshHz));
            var from = metrics.Now;
            var results = await Step(presenter, slides.Count, FadeMs, i => i);
            var presenting = metrics.Summarize(from, metrics.Now);
            var afterGc = await Settle();
            figures.Add(new("Memory while presenting", presenting.ToString()));
            figures.Add(new("Memory presenting, after full GC", Describe(afterGc)));
            figures.Add(new("Pre-render while presenting (render thread)", PreRenderer.Timing(results.SelectMany(r => r.PreRendered).ToList())));
            var worst = Math.Max(presenting.WorkingSetMaxMb, afterGc.WorkingSetMb);
            figures.Add(new($"PLT-005: working set ≤ {MemoryLimitMb:F0} MB while presenting", string.Create(C, $"{(worst <= MemoryLimitMb ? "pass" : "FAIL")} ({worst:F0} MB)")));
        }
        finally
        {
            output.Close();
        }
        GC.KeepAlive(thumbnails);
        return figures;
    }

    /// <summary>Steps through <paramref name="count"/> transitions, one every 600 ms.</summary>
    private static async Task<List<TransitionResult>> Step(Presenter presenter, int count, int fadeMs, Func<int, int> index)
    {
        var results = new List<TransitionResult>();
        for (var i = 0; i < count; i++)
        {
            var clock = Stopwatch.StartNew();
            if (await presenter.GoTo(index(i), fadeMs) is { } result)
                results.Add(result);
            var rest = PeriodMs - (int)clock.ElapsedMilliseconds;
            if (rest > 0)
                await Task.Delay(rest);
        }
        return results;
    }

    private static IEnumerable<Figure> Describe(string label, int fadeMs, List<TransitionResult> results, double refreshMs, MetricSummary metrics)
    {
        if (fadeMs == 0)
        {
            yield return new($"{label}: input to frame, pre-rendered slides (PLT-006: < 100 ms)", Summary.Of(results.Where(r => r.OnDemandMs == 0).Select(r => r.InputToFrameMs)).ToString());
        }
        else
        {
            var frames = results.Select(r => r.Frames).ToList();
            yield return new($"{label}: frames per fade", string.Create(C,
                $"avg {frames.Average(f => f.Frames):F1} (a perfect fade has {fadeMs / refreshMs:F0}); dropped avg {frames.Average(f => f.Dropped):F2} per fade, max {frames.Max(f => f.Dropped)}; {frames.Count(f => f.Dropped > 0)} of {frames.Count} fades dropped any"));
            yield return new($"{label}: longest frame interval per fade", Summary.Of(frames.Select(f => f.MaxIntervalMs)).ToString());
        }
        yield return new($"{label}: pre-render", PreRenderer.Timing(results.SelectMany(r => r.PreRendered).ToList()));
        var onDemand = results.Where(r => r.OnDemandMs > 0).ToList();
        if (onDemand.Count > 0)
            yield return new($"{label}: slides not pre-rendered (first slide, wrap-around)", Summary.Of(onDemand.Select(r => r.OnDemandMs)).ToString());
        yield return new($"{label}: CPU and memory", metrics.ToString());
    }

    /// <summary>Each background decoded at output size from the original twice, then from a JPEG cache copy (ARCH 7.3).</summary>
    private List<Figure> DecodeTimes()
    {
        var figures = new List<Figure>
        {
            new("Decode note", "First read is from the disk only if nothing has read the file since Windows started. Cache copy: the background at output size saved as JPEG (quality 90)."),
        };
        var factory = new SlideFactory(W, H);
        var cacheFolder = Path.Combine(AppContext.BaseDirectory, "spike-cache");
        Directory.CreateDirectory(cacheFolder);
        try
        {
            foreach (var path in media.Backgrounds)
            {
                var (iw, ih) = SlideFactory.PixelSize(path);
                var first = Time(() => factory.Decode(path, FitMode.Fill));
                var second = Time(() => factory.Decode(path, FitMode.Fill));

                var copy = Path.Combine(cacheFolder, Path.GetFileNameWithoutExtension(path) + ".jpg");
                var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
                encoder.Frames.Add(BitmapFrame.Create(factory.Background(path, FitMode.Fill)));
                using (var file = File.Create(copy))
                    encoder.Save(file);
                var cached = Time(() => factory.Decode(copy, FitMode.Fill));

                figures.Add(new($"Decode {Path.GetFileName(path)}", string.Create(C,
                    $"{iw}×{ih}, {new FileInfo(path).Length / 1024} KB: first {first:F0} ms, second {second:F0} ms; cache copy ({new FileInfo(copy).Length / 1024} KB) {cached:F0} ms")));
            }
        }
        finally
        {
            Directory.Delete(cacheFolder, recursive: true);
        }
        return figures;
    }

    private static double Time(Action work)
    {
        var clock = Stopwatch.StartNew();
        work();
        return clock.Elapsed.TotalMilliseconds;
    }

    private static void Collect()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>Full GC, then waits for a fresh metrics sample.</summary>
    private async Task<MetricSample> Settle()
    {
        Collect();
        await Task.Delay(Metrics.Interval * 3);
        return metrics.Latest;
    }

    private static string Describe(MetricSample s) =>
        string.Create(C, $"working set {s.WorkingSetMb:F0} MB, private {s.PrivateMb:F0} MB, managed {s.ManagedMb:F0} MB");
}
