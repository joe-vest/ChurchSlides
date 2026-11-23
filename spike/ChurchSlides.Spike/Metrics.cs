using System.Diagnostics;
using System.Windows.Media;

namespace ChurchSlides.Spike;

public readonly record struct MetricSample(
    TimeSpan At,
    double WorkingSetMb,
    double PrivateMb,
    double ManagedMb,
    double CpuPercent);

public readonly record struct MetricSummary(
    int Samples,
    double WorkingSetMaxMb,
    double PrivateMaxMb,
    double ManagedMaxMb,
    double CpuAvgPercent,
    double CpuMaxPercent)
{
    public override string ToString() =>
        $"working set max {WorkingSetMaxMb:F0} MB, private max {PrivateMaxMb:F0} MB, managed max {ManagedMaxMb:F0} MB, CPU avg {CpuAvgPercent:F0}% max {CpuMaxPercent:F0}%";
}

/// <summary>
/// Samples memory and process CPU every 250 ms on a thread-pool timer, so a busy UI thread
/// doesn't skew the figures. CPU % is of the whole machine (all cores = 100%).
/// </summary>
public sealed class Metrics : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);
    private const int MaxSamples = 4 * 60 * 15; // 15 minutes

    private readonly Process process = Process.GetCurrentProcess();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Queue<MetricSample> samples = new();
    private readonly Lock gate = new();
    private readonly Timer timer;
    private TimeSpan lastCpu;
    private TimeSpan lastAt;
    private MetricSample latest;

    public Metrics()
    {
        lastCpu = process.TotalProcessorTime;
        timer = new Timer(_ => Sample(), null, Interval, Interval);
    }

    public TimeSpan Now => clock.Elapsed;

    public MetricSample Latest
    {
        get { lock (gate) return latest; }
    }

    public MetricSummary Summarize(TimeSpan from, TimeSpan to)
    {
        MetricSample[] window;
        lock (gate)
            window = samples.Where(s => s.At >= from && s.At <= to).ToArray();
        if (window.Length == 0)
            return default;
        return new MetricSummary(
            window.Length,
            window.Max(s => s.WorkingSetMb),
            window.Max(s => s.PrivateMb),
            window.Max(s => s.ManagedMb),
            window.Average(s => s.CpuPercent),
            window.Max(s => s.CpuPercent));
    }

    private void Sample()
    {
        lock (gate)
        {
            process.Refresh();
            var at = clock.Elapsed;
            var cpu = process.TotalProcessorTime;
            var wall = (at - lastAt).TotalMilliseconds;
            var cpuPercent = wall > 0
                ? (cpu - lastCpu).TotalMilliseconds / wall / Environment.ProcessorCount * 100
                : 0;
            lastCpu = cpu;
            lastAt = at;
            latest = new MetricSample(
                at,
                process.WorkingSet64 / 1048576.0,
                process.PrivateMemorySize64 / 1048576.0,
                GC.GetTotalMemory(false) / 1048576.0,
                cpuPercent);
            samples.Enqueue(latest);
            if (samples.Count > MaxSamples)
                samples.Dequeue();
        }
    }

    public void Dispose() => timer.Dispose();
}

/// <summary>
/// Counts frames with CompositionTarget.Rendering. Only while switched on: subscribing makes
/// WPF render every frame, which costs CPU, so it's off unless a measurement needs it.
/// </summary>
public sealed class FrameCounter
{
    private readonly Stopwatch window = new();
    private int frames;

    public bool IsOn { get; private set; }
    public double FramesPerSecond { get; private set; }

    public void SetOn(bool on)
    {
        if (on == IsOn)
            return;
        IsOn = on;
        if (on)
        {
            frames = 0;
            window.Restart();
            CompositionTarget.Rendering += OnRendering;
        }
        else
        {
            CompositionTarget.Rendering -= OnRendering;
            FramesPerSecond = 0;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        frames++;
        if (window.ElapsedMilliseconds < 1000)
            return;
        FramesPerSecond = frames * 1000.0 / window.ElapsedMilliseconds;
        frames = 0;
        window.Restart();
    }
}
