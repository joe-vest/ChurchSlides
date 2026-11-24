using System.Diagnostics;
using System.Globalization;
using System.Windows.Media;

namespace ChurchSlides.Spike;

/// <summary>Average, 95th percentile and maximum of a set of millisecond values.</summary>
public readonly record struct Summary(int Count, double Avg, double P95, double Max)
{
    public static Summary Of(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0
            ? default
            : new Summary(sorted.Length, sorted.Average(), sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1], sorted[^1]);
    }

    public override string ToString() => Count == 0
        ? "none"
        : string.Create(CultureInfo.InvariantCulture, $"avg {Avg:F1} ms, 95% {P95:F1} ms, max {Max:F1} ms (n={Count})");
}

/// <summary>Frames during one transition. Dropped counts the refresh intervals a late frame skipped.</summary>
public readonly record struct FrameStats(int Frames, int Dropped, double MaxIntervalMs)
{
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Frames} frames, {Dropped} dropped, longest interval {MaxIntervalMs:F1} ms");
}

/// <summary>
/// One-off frame waits for the Cue (S4) and Timer (S5) runs: subscribes to CompositionTarget.Rendering
/// only until the frame comes, so WPF isn't made to draw every frame the rest of the time.
/// </summary>
public static class NextFrame
{
    /// <summary>
    /// Completes at the n-th new frame from now with <paramref name="clock"/>'s time. Rendering fires
    /// just before a frame is drawn, so n = 2 is the frame after the one with the change: an upper
    /// bound for "on screen", as S2 uses.
    /// </summary>
    public static Task<double> After(int n, Func<double> clock)
    {
        var done = new TaskCompletionSource<double>();
        var last = TimeSpan.MinValue;
        void OnRendering(object? sender, EventArgs e)
        {
            var time = ((RenderingEventArgs)e).RenderingTime;
            if (time == last)
                return; // the same frame again
            last = time;
            if (--n > 0)
                return;
            CompositionTarget.Rendering -= OnRendering;
            done.TrySetResult(clock());
        }
        CompositionTarget.Rendering += OnRendering;
        return done.Task;
    }
}

/// <summary>
/// Frame timestamps from CompositionTarget.Rendering, on only during a transition (while on,
/// WPF draws every frame). WPF ticks animations on the UI thread just before this event, so a
/// late event means a late animation frame.
/// </summary>
public sealed class FrameMonitor(int refreshHz)
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly List<double> frames = [];
    private TimeSpan lastRenderingTime;
    private (int Count, TaskCompletionSource<double> Done)? waiter;

    public double RefreshMs { get; } = 1000.0 / (refreshHz > 0 ? refreshHz : 60);
    public double Now => clock.Elapsed.TotalMilliseconds;

    public void Begin()
    {
        frames.Clear();
        lastRenderingTime = default;
        CompositionTarget.Rendering += OnRendering;
    }

    public void End()
    {
        CompositionTarget.Rendering -= OnRendering;
        waiter?.Done.TrySetResult(Now);
        waiter = null;
    }

    /// <summary>Completes at the n-th frame from now, with its time.</summary>
    public Task<double> AfterFrames(int n)
    {
        var done = new TaskCompletionSource<double>();
        waiter = (n, done);
        return done.Task;
    }

    /// <summary>Frames after <paramref name="from"/> up to <paramref name="to"/>; the first interval starts at <paramref name="from"/>.</summary>
    public FrameStats Stats(double from, double to)
    {
        var times = frames.Where(t => t > from && t <= to).ToList();
        var dropped = 0;
        var max = 0.0;
        var previous = from;
        foreach (var t in times)
        {
            var interval = t - previous;
            max = Math.Max(max, interval);
            if (interval > 1.5 * RefreshMs)
                dropped += Math.Max(1, (int)Math.Round(interval / RefreshMs) - 1);
            previous = t;
        }
        return new FrameStats(times.Count, dropped, max);
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // Rendering can fire more than once per frame; the same RenderingTime means the same frame.
        var renderingTime = ((RenderingEventArgs)e).RenderingTime;
        if (renderingTime == lastRenderingTime)
            return;
        lastRenderingTime = renderingTime;
        var now = Now;
        frames.Add(now);

        if (waiter is { } w)
        {
            if (w.Count <= 1)
            {
                waiter = null;
                w.Done.TrySetResult(now);
            }
            else
            {
                waiter = (w.Count - 1, w.Done);
            }
        }
    }
}
