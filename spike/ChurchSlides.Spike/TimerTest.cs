using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Threading;

namespace ChurchSlides.Spike;

/// <summary>The Timer tab's choices (S5).</summary>
public sealed record TimerSettings(string Font, double SizePercent, int DurationSeconds, int WarningSeconds, double RunMinutes);

/// <summary>
/// S5 (C0.4b): a countdown on the Output from the duration with the warning color at the threshold,
/// carrying on negative, for the run length. The tick loop runs on the UI thread, re-armed each tick
/// to the next whole second of the system clock (ARCH 6.4, TMR-011), and redraws only the live slot
/// (TMR-010). A plain 1 s DispatcherTimer counts alongside to show the drift the product avoids.
/// For the second half the Output crossfades between two Timer slides every 15 s (decision 7).
/// </summary>
public sealed partial class TimerTest(MonitorInfo monitor, Media media, Metrics metrics, TimerSettings settings, string? s2Fades, Action<string> status)
{
    private const int FadeEverySeconds = 15;
    private const double TickToFramePassMs = 100; // S5-a
    private const double RedrawPassMs = 2; // S5-b

    private static readonly CultureInfo C = CultureInfo.InvariantCulture;
    private static readonly string[] ComparedFonts = ["Segoe UI", "Arial", "Calibri", "Verdana", "Tahoma", "Times New Roman", "Georgia", "Consolas"];

    /// <summary>One tick: the whole second it's for, when it ran, the value shown, redraw time and its frame (all system-clock seconds).</summary>
    private sealed record Tick(double Second, double At, int Value, double RedrawMs, bool Fading, Task<double> Frame);

    private static double Now => DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;

    public async Task<List<Figure>> Run()
    {
        var font = new FontFamily(settings.Font);
        var h = monitor.Bounds.Height;
        var sizePx = h * settings.SizePercent / 100;
        var backgrounds = media.Backgrounds;
        TimerSlide[] content =
        [
            new(backgrounds.ElementAtOrDefault(0), "The service starts in"),
            new(backgrounds.ElementAtOrDefault(1) ?? backgrounds.ElementAtOrDefault(0), "Welcome! We begin in"),
        ];
        var figures = new List<Figure>
        {
            new("Timer setup", string.Create(C,
                $"{settings.Font} {sizePx:F0} px ({settings.SizePercent:F0}% of {h}), countdown from {TimerSlotLayout.Format(settings.DurationSeconds)}, warning at {TimerSlotLayout.Format(settings.WarningSeconds)}, then negative; " +
                $"{settings.RunMinutes:F1} min on {monitor.DeviceName} {monitor.Bounds.Width}×{h} {monitor.RefreshHz} Hz, crossfading ({SlideTests.FadeMs} ms) every {FadeEverySeconds} s for the second half; " +
                $"{(backgrounds.Count > 0 ? "over the media backgrounds" : "over plain blue (no backgrounds in the media folder)")}")),
            new("Digit advance widths (how far proportional digits would move without cells)", DigitSpreads(settings.Font, sizePx)),
        };

        var output = new TimerOutput(monitor, content, font, sizePx);
        if (!await output.ShowAndWait())
        {
            output.Close();
            figures.Add(new("Output window", "did not render within 5 s"));
            return figures;
        }
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        output.Closed += (_, _) => closed.TrySetResult();
        await output.Show(0, 0);

        // Start on a whole second, so every value changes on a whole second.
        await Task.Delay(TimeSpan.FromSeconds(Math.Ceiling(Now) - Now));
        var start = Math.Ceiling(Now - 0.5);
        var ends = start + settings.DurationSeconds;
        var runEnd = start + settings.RunMinutes * 60;
        var fadesFrom = start + settings.RunMinutes * 30;
        var ticks = new List<Tick>();
        var frames = new FrameMonitor(monitor.RefreshHz);
        var fadeStats = new List<FrameStats>();
        var fading = false;
        var nextFade = fadesFrom;
        var halfAt = metrics.Now + TimeSpan.FromMinutes(settings.RunMinutes / 2);
        var from = metrics.Now;
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // The plain interval timer the product avoids: it falls behind by its own lateness every tick.
        var plainTicks = 0;
        var plainStart = Now;
        var plainLast = plainStart;
        var plain = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (_, _) =>
        {
            plainTicks++;
            plainLast = Now;
        }, output.Dispatcher);
        plain.Start();

        var timer = new DispatcherTimer(DispatcherPriority.Normal, output.Dispatcher);
        var due = start; // the whole second the next tick is for
        var earlyWakes = new List<double>();
        async void OnTick(object? sender, EventArgs e)
        {
            timer.Stop();
            var at = Now;
            if (at < due)
            {
                // DispatcherTimer can wake before its time (Windows timer granularity): wait out the rest.
                earlyWakes.Add((due - at) * 1000);
                timer.Interval = TimeSpan.FromSeconds(due - at + 0.001);
                timer.Start();
                return;
            }
            var second = Math.Floor(at - start) + start;
            due = second + 1;
            var value = (int)Math.Ceiling(ends - at);
            var redraw = output.SetValue(TimerSlotLayout.Format(value), value <= settings.WarningSeconds);
            ticks.Add(new Tick(second, at, value, redraw, fading, NextFrame.After(2, () => Now)));
            if (at >= runEnd || closed.Task.IsCompleted)
            {
                finished.TrySetResult();
                return;
            }
            timer.Interval = TimeSpan.FromSeconds(Math.Max(0.001, due - Now + 0.001));
            timer.Start();
            if (at >= nextFade && !fading)
            {
                nextFade += FadeEverySeconds;
                fading = true;
                frames.Begin();
                var fadeFrom = frames.Now;
                await output.Show(1 - output.Slide, SlideTests.FadeMs);
                fadeStats.Add(frames.Stats(fadeFrom, frames.Now));
                frames.End();
                fading = false;
            }
        }
        timer.Tick += OnTick;
        timer.Interval = TimeSpan.FromMilliseconds(1);
        timer.Start();

        var report = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
            status(string.Create(C, $"S5: timer {(ticks.Count > 0 ? TimerSlotLayout.Format(ticks[^1].Value) : "")}, {Math.Max(0, runEnd - Now) / 60:F1} min left (Esc on the Output stops it)…")), output.Dispatcher);
        report.Start();
        await Task.WhenAny(finished.Task, closed.Task);
        report.Stop();
        timer.Stop();
        plain.Stop();
        var to = metrics.Now;
        var shownFrames = await Task.WhenAll(ticks.Select(t => Task.WhenAny(t.Frame, Task.Delay(1000)).ContinueWith(_ => t.Frame.IsCompleted ? t.Frame.Result : double.NaN, TaskScheduler.Default)));
        output.Close();

        figures.AddRange(Describe(ticks, shownFrames, start, runEnd, plainTicks, plainLast - plainStart, fadeStats, metrics.Summarize(from, halfAt), metrics.Summarize(halfAt, to)));
        figures.Insert(figures.Count - 2, new("DispatcherTimer woke before the whole second (re-armed for the rest)", string.Create(C,
            $"{earlyWakes.Count} of {ticks.Count} ticks{(earlyWakes.Count > 0 ? $", by {Summary.Of(earlyWakes)}" : "")}. The product's tick loop must check the time and wait again, or it shows a value twice.")));
        return figures;
    }

    private IEnumerable<Figure> Describe(
        List<Tick> ticks, double[] shownFrames, double start, double runEnd, int plainTicks, double plainSeconds,
        List<FrameStats> fades, MetricSummary quiet, MetricSummary fading)
    {
        if (ticks.Count == 0)
        {
            yield return new("Ticks", "none");
            yield break;
        }
        var last = ticks[^1].At;
        if (last < runEnd - 1)
            yield return new("Stopped early", string.Create(C, $"after {(last - start) / 60:F1} min (the Output was closed)"));

        // Values: each tick should show one less than the one before.
        int skipped = 0, doubled = 0;
        for (var i = 1; i < ticks.Count; i++)
        {
            var step = ticks[i - 1].Value - ticks[i].Value;
            if (step == 0)
                doubled++;
            else if (step > 1)
                skipped += step - 1;
        }
        yield return new("Values", string.Create(C,
            $"{ticks.Count} ticks from {TimerSlotLayout.Format(ticks[0].Value)} to {TimerSlotLayout.Format(ticks[^1].Value)}; seconds skipped {skipped}, shown twice {doubled}"));

        var late = ticks.Select((t, i) => (t, Ms: (shownFrames[i] - t.Second) * 1000)).Where(x => double.IsFinite(x.Ms)).ToList();
        var toFrameQuiet = Summary.Of(late.Where(x => !x.t.Fading && x.t.At < start + (runEnd - start) / 2).Select(x => x.Ms));
        var toFrameFading = Summary.Of(late.Where(x => x.t.At >= start + (runEnd - start) / 2).Select(x => x.Ms));
        yield return new("Whole second to the frame showing it, first half (no fades)", toFrameQuiet.ToString());
        yield return new("Whole second to the frame showing it, second half (fades)", toFrameFading.ToString());
        yield return new("Whole second to tick", Summary.Of(ticks.Select(t => (t.At - t.Second) * 1000)).ToString());
        var redraw = Summary.Of(ticks.Select(t => t.RedrawMs));
        yield return new("Slot redraw per tick (visible slots)", redraw.ToString());

        var plainMs = (plainSeconds - plainTicks) * 1000;
        yield return new("Plain 1 s DispatcherTimer (not shown)", string.Create(C,
            $"{plainTicks} ticks, {plainMs:F0} ms behind the wall clock at its last tick ({(plainTicks > 0 ? plainMs / plainTicks : 0):F1} ms per tick)"));

        var warnFrom = ticks.FirstOrDefault(t => t.Value <= settings.WarningSeconds);
        var negative = ticks.FirstOrDefault(t => t.Value < 0);
        yield return new("Warning and minus (TMR-005, TMR-006)",
            $"warning color from {(warnFrom is null ? "not reached" : TimerSlotLayout.Format(warnFrom.Value))}; first negative value {(negative is null ? "not reached" : TimerSlotLayout.Format(negative.Value))}");

        yield return new("CPU and memory, first half (no fades)", quiet.ToString());
        yield return new("CPU and memory, second half (fades)", fading.ToString());
        var fadeLine = fades.Count == 0 ? "no fades" : string.Create(C,
            $"{fades.Count} fades: avg {fades.Average(f => f.Frames):F1} frames, dropped avg {fades.Average(f => f.Dropped):F2} per fade, max {fades.Max(f => f.Dropped)}; longest interval {Summary.Of(fades.Select(f => f.MaxIntervalMs))}");
        yield return new("Crossfades with the timer", fadeLine);
        yield return new("S2 for comparison (render thread)", (s2Fades ?? "S2 not measured yet") +
            $". S2's fades come 0.6 s apart, these {FadeEverySeconds} s apart: on the development PC fades 1 s apart dropped as few as S2 and fades 15 s apart about 5 per fade, timer or not (C0.4b), so a gap between fades costs frames.");

        var maxLate = Math.Max(toFrameQuiet.Max, toFrameFading.Max);
        yield return new($"S5-a: no second skipped or doubled, each shown ≤ {TickToFramePassMs:F0} ms after its whole second",
            string.Create(C, $"{(skipped == 0 && doubled == 0 && maxLate <= TickToFramePassMs ? "pass" : "FAIL")} (skipped {skipped}, doubled {doubled}, latest {maxLate:F1} ms)"));
        var s2Dropped = s2Fades is not null && DroppedAvg().Match(s2Fades) is { Success: true } m ? double.Parse(m.Groups[1].Value, C) : double.NaN;
        var dropped = fades.Count == 0 ? double.NaN : fades.Average(f => f.Dropped);
        var fadeVerdict = double.IsNaN(s2Dropped) || double.IsNaN(dropped) ? "no S2 figure to compare"
            : dropped <= s2Dropped ? string.Create(C, $"fades drop {dropped:F2} per fade against S2's {s2Dropped:F2}")
            : string.Create(C, $"FAIL: fades drop {dropped:F2} per fade against S2's {s2Dropped:F2}");
        yield return new($"S5-b: slot redraw < {RedrawPassMs:F0} ms, fades drop no more than S2",
            string.Create(C, $"redraw {(redraw.Max < RedrawPassMs ? "pass" : "FAIL")} (max {redraw.Max:F2} ms); {fadeVerdict}; CPU avg {quiet.CpuAvgPercent:F1}% without fades, {fading.CpuAvgPercent:F1}% with"));
    }

    private static string DigitSpreads(string chosen, double sizePx)
    {
        var installed = Fonts.SystemFontFamilies.Select(f => f.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = ComparedFonts.Prepend(chosen).Distinct(StringComparer.OrdinalIgnoreCase).Where(installed.Contains);
        return string.Join("; ", names.Select(n => string.Create(C, $"{n} {TimerSlotLayout.DigitSpread(new FontFamily(n), sizePx):F1} px")))
            + string.Create(C, $" (spread between the widest and narrowest digit at {sizePx:F0} px)");
    }

    [GeneratedRegex(@"dropped avg ([\d.]+)")]
    private static partial Regex DroppedAvg();
}
