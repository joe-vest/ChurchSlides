using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace ChurchSlides.Spike;

/// <summary>
/// The outputs compared (C0.4 decision 4). Default device only, shared mode only (AUD-002). NAudio 3.x
/// marks WasapiOut obsolete, so WasapiPlayer stands in for it (owner, 2026-11-23).
/// </summary>
public enum OutputKind
{
    WaveOut100,
    WasapiPlayer50,
    WasapiPlayer100,
}

public sealed record AudioCombo(ReaderKind Reader, OutputKind Output)
{
    public static readonly AudioCombo[] All =
    [
        .. from r in Enum.GetValues<ReaderKind>() from o in Enum.GetValues<OutputKind>() select new AudioCombo(r, o),
    ];

    public static string Label(OutputKind output) => output switch
    {
        OutputKind.WaveOut100 => "WaveOut 100 ms",
        OutputKind.WasapiPlayer50 => "WasapiPlayer shared 50 ms",
        _ => "WasapiPlayer shared 100 ms",
    };

    public override string ToString() => $"{Reader} + {Label(Output)}";
}

/// <summary>One clock for every audio timing: commands, position samples and loopback capture (QPC based).</summary>
public static class AudioClock
{
    private static readonly long Origin = Stopwatch.GetTimestamp();

    public static double Seconds => (Stopwatch.GetTimestamp() - Origin) / (double)Stopwatch.Frequency;

    /// <summary>A WASAPI QPC position (100 ns units) on this clock.</summary>
    public static double FromQpc(long qpc100ns) => qpc100ns * 1e-7 - Origin / (double)Stopwatch.Frequency;
}

/// <summary>
/// One reader and one output (C0.4a). Plays from a position to an optional end that the sample
/// stream stops at exactly (Trim End), and reports the position two ways: the output's
/// GetPosition (what the device has played) and the reader's CurrentTime (what has been decoded).
/// The file is streamed, never decoded whole.
/// </summary>
public sealed class SpikePlayer : IDisposable
{
    private readonly WaveStream reader;
    private readonly ISampleProvider samples;
    private readonly SwitchableProvider feed;
    private IWavePlayer output;
    private TaskCompletionSource<Exception?>? stopping;
    private volatile bool stoppingOnPurpose;
    private double startedAt;
    private long basePosition;

    /// <param name="followDefault">WasapiPlayer only: follow the default device with Windows stream routing.</param>
    public SpikePlayer(string path, AudioCombo combo, bool followDefault = false)
        : this(AudioFiles.Open(path, combo.Reader), combo, followDefault)
    {
    }

    /// <summary>Plays any stream (the Sync check's click track); the combo's reader is then only a label.</summary>
    public SpikePlayer(WaveStream reader, AudioCombo combo, bool followDefault = false)
    {
        Combo = combo;
        FollowDefault = followDefault && combo.Output != OutputKind.WaveOut100;
        this.reader = reader;
        samples = reader.ToSampleProvider();
        feed = new SwitchableProvider(samples.WaveFormat);
        output = CreateOutput();
    }

    public AudioCombo Combo { get; }
    public bool FollowDefault { get; }
    public TimeSpan Length => reader.TotalTime;
    public bool IsPlaying => output.PlaybackState == PlaybackState.Playing;

    /// <summary>The output's stop event: at Trim End, on Stop, or on a device error (with the exception). On the UI thread.</summary>
    public event Action<Exception?>? Stopped;

    /// <summary>File position from the output device's played-sample count, in seconds.</summary>
    public double OutputSeconds
    {
        get
        {
            var position = (IWavePosition)output;
            var played = position.GetPosition();
            var from = Interlocked.Read(ref basePosition);
            if (played < from)
            {
                // The device reset its count when it started.
                from = 0;
                Interlocked.Exchange(ref basePosition, 0);
            }
            return Volatile.Read(ref startedAt) + (played - from) / (double)position.OutputWaveFormat.AverageBytesPerSecond;
        }
    }

    /// <summary>File position from the reader (ahead of what is heard by the output's buffers), in seconds.</summary>
    public double ReaderSeconds => reader.CurrentTime.TotalSeconds;

    /// <summary>The output position, or the reader's when the output can't answer (device gone).</summary>
    public double SafeSeconds()
    {
        try
        {
            return OutputSeconds;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException or NAudio.MmException)
        {
            return ReaderSeconds;
        }
    }

    /// <summary>Stops if playing, seeks the reader, and plays until <paramref name="until"/> (or the end).</summary>
    public async Task PlayFrom(double seconds, double? until)
    {
        await StopAsync();
        reader.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, reader.TotalTime.TotalSeconds));
        var from = reader.CurrentTime.TotalSeconds;
        Volatile.Write(ref startedAt, from);
        var format = samples.WaveFormat;
        long? count = until is { } end ? (long)Math.Max(0, (end - from) * format.SampleRate) * format.Channels : null;
        feed.Source = new TrimmedProvider(samples, count);
        stopping = null;
        // WaveOut's played-sample count isn't reset when the stream ends by itself (only Stop resets it),
        // so the position counts from what the device says now.
        Interlocked.Exchange(ref basePosition, ((IWavePosition)output).GetPosition());
        output.Play();
    }

    /// <summary>Stops and waits (up to 1 s) for the output's stop event, so the reader is free to move.</summary>
    public async Task StopAsync()
    {
        if (output.PlaybackState == PlaybackState.Stopped)
            return;
        var done = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        stopping = done;
        stoppingOnPurpose = true;
        output.Stop();
        await Task.WhenAny(done.Task, Task.Delay(1000));
        stoppingOnPurpose = false;
    }

    /// <summary>Recreates the output on the current default device and plays on from <paramref name="seconds"/>. Returns the time taken.</summary>
    public async Task<double> ResumeOnNewDevice(double seconds)
    {
        var clock = Stopwatch.StartNew();
        var old = output;
        old.PlaybackStopped -= OnStopped;
        try
        {
            old.Dispose();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NAudio.MmException)
        {
            // The old device is gone; nothing to release.
        }
        output = CreateOutput();
        await PlayFrom(seconds, null);
        return clock.Elapsed.TotalMilliseconds;
    }

    public static string Clock(double seconds) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)(seconds / 60)}:{seconds % 60:00.0}");

    public void Dispose()
    {
        output.PlaybackStopped -= OnStopped;
        try
        {
            output.Dispose();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NAudio.MmException)
        {
            // Device already gone.
        }
        reader.Dispose();
    }

    private IWavePlayer CreateOutput()
    {
        IWavePlayer player = Combo.Output switch
        {
            // NAudio 3.x renamed WaveOutEvent to WaveOut and sets its buffers directly: two of 50 ms is 2.x "DesiredLatency 100".
            OutputKind.WaveOut100 => new WaveOut { NumberOfBuffers = 2, BufferMilliseconds = 50 },
            _ => Wasapi(Combo.Output == OutputKind.WasapiPlayer50 ? 50 : 100),
        };
        player.Init(feed);
        player.PlaybackStopped += OnStopped;
        return player;
    }

    private WasapiPlayer Wasapi(int latencyMs)
    {
        var builder = new WasapiPlayerBuilder().WithSharedMode().WithEventSync().WithLatency(latencyMs);
        if (FollowDefault)
            builder.WithDefaultDeviceStreamRouting();
        return builder.Build();
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        stopping?.TrySetResult(e.Exception);
        if (!stoppingOnPurpose || e.Exception is not null)
            Stopped?.Invoke(e.Exception);
    }

    /// <summary>The output reads through this, so a new trim can be set without re-initialising the output.</summary>
    private sealed class SwitchableProvider(WaveFormat format) : IWaveProvider
    {
        private readonly WaveFormat format = WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels);

        public ISampleProvider? Source { get; set; }
        public WaveFormat WaveFormat => format;

        public int Read(Span<byte> buffer)
        {
            if (Source is not { } source)
                return 0;
            var floats = MemoryMarshal.Cast<byte, float>(buffer);
            return source.Read(floats) * sizeof(float);
        }
    }

    /// <summary>Ends after exactly <paramref name="count"/> samples (Trim End), or runs to the end of the file.</summary>
    private sealed class TrimmedProvider(ISampleProvider source, long? count) : ISampleProvider
    {
        private long? left = count;

        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(Span<float> buffer)
        {
            if (left is { } l)
            {
                if (l <= 0)
                    return 0;
                if (buffer.Length > l)
                    buffer = buffer[..(int)l];
            }
            var read = source.Read(buffer);
            left -= read;
            return read;
        }
    }
}
