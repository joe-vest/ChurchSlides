using System.Globalization;
using System.Runtime.InteropServices;

namespace ChurchSlides.Spike;

/// <summary>
/// S4-c (C0.4b): a song plays with a Cue every 2 s, each to its own slide on the Output. The poller
/// thread reads the best combination's output position less its offset and posts each Cue to the UI
/// thread, which shows the slide (Cut). Meanwhile slides pre-render every 500 ms on the UI thread or
/// the render thread, each from a fresh factory (background decoded and placed: the costly case).
/// Lateness is from the moment the Cue's position is heard (loopback) to the frame showing its slide.
/// Four runs of 40 s: pre-render on each thread, at the default timer resolution and with timeBeginPeriod(1).
/// </summary>
public sealed class CueTest(MonitorInfo monitor, Media media, string song, RenderThread thread, Action<string> status)
{
    private const int CueCount = 20;
    private const double CueSpacing = 2.0;
    private const int PreRenderEveryMs = 500;
    private const double ProbeSeconds = 0.5;
    private const double LatenessPassMs = 50; // S4-c, AUD-018

    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    private int W => monitor.Bounds.Width;
    private int H => monitor.Bounds.Height;

    private sealed record CueRun(string Label, Summary Lateness, List<Figure> Figures);

    /// <summary>One Cue as it went: when the poller saw it, when the UI thread ran it, and its frame.</summary>
    private sealed record Shown(int Index, double Seen, double Handled, Task<double> Frame, bool OnDemand);

    public async Task<List<Figure>> Run()
    {
        var combo = CueOffsets.Best;
        var slides = TestContent.HymnSlides(media.Backgrounds, FitMode.Fill);
        var figures = new List<Figure>
        {
            new("Cue setup", $"{Path.GetFileName(song)}, {combo}, {CueOffsets.Describe(combo)}; {CueCount} Cues {CueSpacing:F0} s apart, each to its own slide (Cut) on {monitor.DeviceName} {W}×{H}; " +
                $"a slide pre-rendered every {PreRenderEveryMs} ms from a fresh factory. Lateness: Cue position heard (loopback) to the frame after the one with the change (an upper bound, as S2)."),
        };

        status("S4 Cues: decoding the reference…");
        var reference = await Task.Run(() => AudioFiles.Decode(song, combo.Reader));
        var output = new SlideOutput(monitor, null);
        if (!await output.ShowAndWait())
        {
            output.Close();
            figures.Add(new("Output window", "did not render within 5 s"));
            return figures;
        }
        var runs = new List<CueRun>();
        try
        {
            foreach (var (onThread, fine) in new[] { (false, false), (false, true), (true, false), (true, true) })
            {
                var label = $"Pre-render on {(onThread ? "render thread" : "UI thread")}, {(fine ? "timeBeginPeriod(1)" : "default timer")}";
                status($"S4 Cues: {label}…");
                try
                {
                    var run = await One(label, output, slides, reference, combo, onThread ? thread : null, fine);
                    runs.Add(run);
                    figures.AddRange(run.Figures);
                }
                catch (Exception ex) when (ex is COMException or InvalidOperationException or NAudio.MmException)
                {
                    figures.Add(new(label, $"failed: {ex.GetType().Name}: {ex.Message}"));
                }
            }
        }
        finally
        {
            output.Close();
        }

        var measured = runs.Where(r => r.Lateness.Count > 0).ToList();
        figures.Add(new($"S4-c: Cue heard to slide shown ≤ {LatenessPassMs:F0} ms at p95, with pre-render", measured.Count == 0
            ? "no Cue measured"
            : string.Join("; ", measured.Select(r => string.Create(C, $"{r.Label} {(r.Lateness.P95 <= LatenessPassMs ? "pass" : "FAIL")} (p95 {r.Lateness.P95:F1} ms)")))));
        return figures;
    }

    private async Task<CueRun> One(string label, SlideOutput output, List<SlideSpec> slides, Reference reference, AudioCombo combo, RenderThread? renderOn, bool fine)
    {
        using var fineTimer = fine ? new FineTimer() : null;
        using var probe = LoopbackProbe.Start();
        using var player = new SpikePlayer(song, combo);
        var start = Math.Min(10, player.Length.TotalSeconds * 0.1);
        var cues = Enumerable.Range(0, CueCount).Select(k => start + 1 + k * CueSpacing).Where(t => t < player.Length.TotalSeconds - 1).ToList();
        var offsetSeconds = CueOffsets.Ms(combo) / 1000;

        // The slides for the first Cues are ready before Play, as the product's pre-render window would be.
        var ready = new Dictionary<int, RenderedSlide>();
        for (var i = 0; i < 2; i++)
            ready[i] = await Render(slides, i, renderOn);
        await Task.Delay(500);

        var shown = new List<Shown>();
        void OnCue(int index, double seen)
        {
            var handled = AudioClock.Seconds;
            var onDemand = !ready.Remove(index, out var slide);
            slide ??= new SlideFactory(W, H).Render(slides[index % slides.Count]);
            _ = output.Show(slide, 0);
            shown.Add(new Shown(index, seen, handled, NextFrame.After(2, () => AudioClock.Seconds), onDemand));
        }

        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        player.Stopped += _ => ended.TrySetResult();
        var playAt = AudioClock.Seconds;
        await player.PlayFrom(start, cues[^1] + 1);
        var renders = new List<double>();
        Summary intervals;
        using (var poller = new CuePoller(() => player.OutputSeconds - offsetSeconds, cues, OnCue))
        {
            var timeout = Task.Delay(TimeSpan.FromSeconds(cues[^1] - start + 10));
            while (!ended.Task.IsCompleted && !timeout.IsCompleted)
            {
                // Pre-render the slide for the next Cue again, so a render is always under way somewhere.
                var next = shown.Count;
                foreach (var old in ready.Keys.Where(k => k < next).ToList())
                    ready.Remove(old);
                var slide = await Render(slides, next, renderOn);
                renders.Add(slide.Timing.TotalMs);
                if (next >= shown.Count)
                    ready[next] = slide;
                await Task.WhenAny(ended.Task, timeout, Task.Delay(PreRenderEveryMs));
            }
            intervals = poller.Intervals;
        }
        await player.StopAsync();
        await Task.Delay(300);
        var recording = probe.Stop();
        var frames = new List<double>();
        for (var i = 0; i < shown.Count; i++)
            frames.Add(await shown[i].Frame);

        var figures = new List<Figure>();
        if (recording.IsSilent)
        {
            figures.Add(new(label, "loopback heard nothing. Is the volume muted? Keep it low but not muted."));
            return new CueRun(label, default, figures);
        }
        return await Task.Run(() => Analyse(label, reference, recording, playAt, start, cues, shown, frames, renders, intervals));
    }

    private Task<RenderedSlide> Render(List<SlideSpec> slides, int index, RenderThread? renderOn) =>
        new PreRenderer(new SlideFactory(W, H), renderOn).Render(slides[index % slides.Count]);

    private static CueRun Analyse(
        string label, Reference reference, Recording rec, double playAt, double start, List<double> cues,
        List<Shown> shown, List<double> frames, List<double> renders, Summary intervals)
    {
        var figures = new List<Figure>();
        var window = Reference.Index(ProbeSeconds);
        var onset = rec.FirstSound(rec.IndexAt(playAt), rec.Samples.Length);
        var offset = rec.LastSound(onset, rec.Samples.Length);
        if (onset < 0 || offset - onset < 2 * window)
        {
            figures.Add(new(label, "the song wasn't heard through loopback"));
            return new CueRun(label, default, figures);
        }
        var first = Correlation.Find(rec.Samples.AsSpan(onset, window), reference.Samples, Reference.Index(start - 1), Reference.Index(start + 1));
        var heard = HeardTimeline.Build(rec, reference, onset, offset, Reference.ToSeconds(first.Index), window);

        var late = new List<double>();
        var toPoller = new List<double>();
        var toUi = new List<double>();
        var toFrame = new List<double>();
        for (var i = 0; i < shown.Count; i++)
        {
            var s = shown[i];
            var heardAt = heard.ClockSecondsOf(cues[s.Index]);
            if (!double.IsFinite(heardAt))
                continue;
            late.Add((frames[i] - heardAt) * 1000);
            toPoller.Add((s.Seen - heardAt) * 1000);
            toUi.Add((s.Handled - s.Seen) * 1000);
            toFrame.Add((frames[i] - s.Handled) * 1000);
        }
        var lateness = Summary.Of(late);
        var onDemand = shown.Count(s => s.OnDemand);
        figures.Add(new($"{label}: Cue heard to slide shown", string.Create(C,
            $"{lateness}{(late.Count > 0 ? $", earliest {late.Min():F1} ms" : "")}; {shown.Count} of {cues.Count} Cues fired{(onDemand > 0 ? $", {onDemand} not pre-rendered (rendered when the Cue came)" : "")}")));
        figures.Add(new($"{label}: parts", string.Create(C,
            $"heard to poller {Summary.Of(toPoller)}; poller to UI thread {Summary.Of(toUi)}; UI thread to frame {Summary.Of(toFrame)}; poller loop (sleep 10 ms) {intervals}")));
        figures.Add(new($"{label}: pre-render while playing", string.Create(C,
            $"{Summary.Of(renders)}; heard timeline {heard.Count} of {heard.Windows} windows matched, {heard.Steps} jumps over 5 ms")));
        return new CueRun(label, lateness, figures);
    }
}
