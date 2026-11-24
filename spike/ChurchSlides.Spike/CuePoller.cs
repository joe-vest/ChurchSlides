using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using NAudio.Wave;

namespace ChurchSlides.Spike;

/// <summary>
/// The latency offset a Cue subtracts from the output position (S4-b), per reader and output, and
/// the combination the Cue runs use. Filled in by the audio test; until it runs, the development
/// PC's C0.4a figures stand in.
/// </summary>
public static class CueOffsets
{
    // Development PC, C0.4a (STATUS): output position minus heard, the same for every output.
    private const double Mp3FileReaderMs = 1.1;
    private const double MediaFoundationReaderMs = 6.4;

    private static readonly Dictionary<AudioCombo, double> Measured = [];

    /// <summary>
    /// The combination with the smallest p95 of output position minus heard (less its offset), among
    /// readers that passed S4-a when any did: a reader that seeks badly starts Cues in the wrong place.
    /// </summary>
    public static AudioCombo Best { get; private set; } = new(ReaderKind.Mp3FileReader, OutputKind.WasapiPlayer50);

    public static void Record(IEnumerable<(AudioCombo Combo, double OffsetMs, Summary Residual)> runs, IReadOnlySet<ReaderKind> seekPassed)
    {
        var heard = runs.Where(r => double.IsFinite(r.OffsetMs) && r.Residual.Count > 0).ToList();
        foreach (var r in heard)
            Measured[r.Combo] = r.OffsetMs;
        var candidates = heard.Any(r => seekPassed.Contains(r.Combo.Reader)) ? heard.Where(r => seekPassed.Contains(r.Combo.Reader)).ToList() : heard;
        if (candidates.Count > 0)
            Best = candidates.MinBy(r => r.Residual.P95).Combo;
    }

    public static double Ms(AudioCombo combo) =>
        Measured.TryGetValue(combo, out var ms) ? ms
        : combo.Reader == ReaderKind.Mp3FileReader ? Mp3FileReaderMs : MediaFoundationReaderMs;

    public static string Describe(AudioCombo combo) => string.Create(CultureInfo.InvariantCulture,
        $"offset {Ms(combo):+0.0;-0.0;0.0} ms ({(Measured.ContainsKey(combo) ? "measured by this session's audio test" : "the development PC's figure; run the audio test first to measure it on this PC")})");
}

/// <summary>
/// The Cue poller (ARCH 4.1, C0.4 decision 5): a dedicated thread reads the playing position about
/// every 10 ms and, when it passes the next Cue, posts that Cue to the UI thread at Normal priority
/// (above rendering and input). Each Cue fires once, in order.
/// </summary>
public sealed class CuePoller : IDisposable
{
    private const int SleepMs = 10;

    private readonly Func<double> position;
    private readonly IReadOnlyList<double> cues;
    private readonly Action<int, double> fire;
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly List<double> intervals = [];
    private readonly Thread thread;
    private volatile bool stop;

    /// <param name="position">File position in seconds, less the latency offset. Read on the poller thread.</param>
    /// <param name="cues">Cue file times in seconds, in order.</param>
    /// <param name="fire">On the UI thread: the Cue's index and the AudioClock time the poller saw it pass.</param>
    public CuePoller(Func<double> position, IReadOnlyList<double> cues, Action<int, double> fire)
    {
        this.position = position;
        this.cues = cues;
        this.fire = fire;
        thread = new Thread(Run) { IsBackground = true, Name = "Cue poller", Priority = ThreadPriority.Highest };
        thread.Start();
    }

    /// <summary>What each "10 ms" sleep really took: about 15.6 ms at the default timer resolution, about 10-11 with timeBeginPeriod(1).</summary>
    public Summary Intervals
    {
        get
        {
            lock (intervals)
                return Summary.Of(intervals);
        }
    }

    public void Dispose()
    {
        stop = true;
        thread.Join();
    }

    private void Run()
    {
        var next = 0;
        var last = AudioClock.Seconds;
        while (!stop && next < cues.Count)
        {
            double at;
            try
            {
                at = position();
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException or NAudio.MmException)
            {
                at = double.NaN; // device gone: wait for the player to recover or stop
            }
            var seen = AudioClock.Seconds;
            while (next < cues.Count && at >= cues[next])
            {
                var index = next++;
                dispatcher.BeginInvoke(DispatcherPriority.Normal, () => fire(index, seen));
            }
            Thread.Sleep(SleepMs);
            var now = AudioClock.Seconds;
            lock (intervals)
                intervals.Add((now - last) * 1000);
            last = now;
        }
    }
}

/// <summary>
/// Raises the Windows timer resolution to 1 ms while alive (C0.4 decision 5). Windows 10 2004 and
/// later apply it to this process only.
/// </summary>
public sealed class FineTimer : IDisposable
{
    public FineTimer() => Native.timeBeginPeriod(1);

    public void Dispose() => Native.timeEndPeriod(1);
}

/// <summary>
/// The Sync ear check's sound (S4-2): a short 2 kHz click on every whole second from 1 s, made as
/// it's read (nothing stored), so a Cue on each whole second should flash with each click.
/// </summary>
public sealed class ClickTrack(TimeSpan length) : WaveStream
{
    private const int Rate = 44100;
    private const double ClickSeconds = 0.015;
    private static readonly WaveFormat Format = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2);

    private readonly long length = (long)(length.TotalSeconds * Rate) * Format.BlockAlign;
    private long position;

    public override WaveFormat WaveFormat => Format;
    public override long Length => length;

    public override long Position
    {
        get => position;
        set => position = Math.Clamp(value / Format.BlockAlign * Format.BlockAlign, 0, length);
    }

    /// <summary>The click times in seconds.</summary>
    public IReadOnlyList<double> Clicks => [.. Enumerable.Range(1, Math.Max(0, (int)TotalTime.TotalSeconds - 1)).Select(s => (double)s)];

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var bytes = (int)Math.Min(buffer.Length / Format.BlockAlign * Format.BlockAlign, length - position);
        var floats = MemoryMarshal.Cast<byte, float>(buffer[..bytes]);
        var frame = position / Format.BlockAlign;
        for (var i = 0; i + 1 < floats.Length; i += 2, frame++)
        {
            var t = frame % Rate / (double)Rate; // seconds since the last whole second
            var v = frame >= Rate && t < ClickSeconds ? (float)(0.5 * Math.Sin(2 * Math.PI * 2000 * t) * (1 - t / ClickSeconds)) : 0f;
            floats[i] = floats[i + 1] = v;
        }
        position += bytes;
        return bytes;
    }
}
