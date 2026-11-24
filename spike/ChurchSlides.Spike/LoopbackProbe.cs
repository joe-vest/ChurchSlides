using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ChurchSlides.Spike;

/// <summary>What the loopback heard, as mono at 11,025 per second, placed on the AudioClock.</summary>
public sealed class Recording(float[] samples, double startSeconds, double clockSpreadMs, int packets, int gaps, int badStamps)
{
    /// <summary>Digital silence: the mixer sends exact zeros when nothing plays (the keep-alive stream is silent).</summary>
    public const float Silence = 1e-5f;

    public float[] Samples { get; } = samples;

    /// <summary>AudioClock time of sample 0, from the first packet with a usable QPC timestamp.</summary>
    public double StartSeconds { get; } = startSeconds;

    /// <summary>The most any packet's QPC timestamp differed from its place by device position (device clock against QPC).</summary>
    public double ClockSpreadMs { get; } = clockSpreadMs;

    public int Packets { get; } = packets;

    /// <summary>Packets flagged as a discontinuity, or missing samples by device position (filled with silence).</summary>
    public int Gaps { get; } = gaps;

    /// <summary>Packets without a usable QPC timestamp (left out of the timing).</summary>
    public int BadStamps { get; } = badStamps;

    public double TimeOf(double index) => StartSeconds + index / MonoDownsampler.Rate;

    public int IndexAt(double seconds) => Math.Clamp((int)Math.Round((seconds - StartSeconds) * MonoDownsampler.Rate), 0, Samples.Length);

    public bool IsSilent => !Samples.Any(s => Math.Abs(s) > Silence);

    /// <summary>The first sample in [from, to) that isn't silence, or -1.</summary>
    public int FirstSound(int from, int to)
    {
        for (var i = Math.Max(0, from); i < Math.Min(to, Samples.Length); i++)
        {
            if (Math.Abs(Samples[i]) > Silence)
                return i;
        }
        return -1;
    }

    /// <summary>The last sample in [from, to) that isn't silence, or -1.</summary>
    public int LastSound(int from, int to)
    {
        for (var i = Math.Min(to, Samples.Length) - 1; i >= Math.Max(0, from); i--)
        {
            if (Math.Abs(Samples[i]) > Silence)
                return i;
        }
        return -1;
    }
}

/// <summary>
/// WASAPI loopback capture of the default device: what leaves the program into the Windows mixer
/// (C0.4 decision 3). Each packet carries the device position and QPC time of its first sample, so
/// sample 0 is placed on the AudioClock exactly and gaps are filled with silence. A silent keep-alive
/// stream runs alongside, because loopback delivers no packets while nothing plays.
/// </summary>
public sealed class LoopbackProbe : IDisposable
{
    private readonly WasapiPlayer keepAlive;
    private readonly WasapiRecorder capture;
    private readonly WaveFormat format;
    private readonly MonoDownsampler down;
    private readonly List<float> samples = [];
    private readonly Lock gate = new();
    private float[] scratch = [];
    private long firstPosition = -1;
    private double startSeconds = double.NaN;
    private int badStamps;
    private long frames;
    private int packets;
    private int gaps;
    private double spread;

    private LoopbackProbe()
    {
        keepAlive = new WasapiPlayerBuilder().WithSharedMode().WithLatency(100).Build();
        keepAlive.Init(new SilenceProvider(new WaveFormat(44100, 16, 2)));
        keepAlive.Play();

        capture = new WasapiRecorderBuilder().WithLoopbackCapture().Build();
        format = capture.WaveFormat;
        if (format.BitsPerSample is not (16 or 32))
            throw new NotSupportedException($"Loopback format {format} isn't 16-bit PCM or 32-bit float.");
        down = new MonoDownsampler(format.SampleRate, format.Channels);
        capture.DataAvailable += OnData;
        capture.StartRecording();
    }

    public string Format => format.ToString();

    public static LoopbackProbe Start() => new();

    /// <summary>Stops capturing and returns what was heard.</summary>
    public Recording Stop()
    {
        capture.StopRecording();
        keepAlive.Stop();
        lock (gate)
            return new Recording([.. samples], startSeconds, spread * 1000, packets, gaps, badStamps);
    }

    public void Dispose()
    {
        capture.DataAvailable -= OnData;
        capture.Dispose();
        keepAlive.Dispose();
    }

    private void OnData(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        var count = buffer.Length / format.BlockAlign;
        var length = count * format.Channels;
        if (scratch.Length < length)
            scratch = new float[length];
        var floats = scratch.AsSpan(0, length);
        if (flags.HasFlag(AudioClientBufferFlags.Silent))
            floats.Clear(); // a buffer flagged silent has undefined contents
        else if (format.BitsPerSample == 32)
            MemoryMarshal.Cast<byte, float>(buffer)[..length].CopyTo(floats);
        else
            Convert16(buffer, floats);

        lock (gate)
        {
            if (firstPosition < 0)
                firstPosition = devicePosition;
            var stamped = qpcPosition > 0 && !flags.HasFlag(AudioClientBufferFlags.TimestampError);
            if (stamped && double.IsNaN(startSeconds))
                startSeconds = AudioClock.FromQpc(qpcPosition) - (devicePosition - firstPosition) / (double)format.SampleRate;
            var missing = devicePosition - firstPosition - frames;
            if (missing > 0)
            {
                gaps++;
                AddSilence(missing);
            }
            if (flags.HasFlag(AudioClientBufferFlags.DataDiscontinuity) && packets > 0)
                gaps++;
            if (stamped)
            {
                var expected = startSeconds + (devicePosition - firstPosition) / (double)format.SampleRate;
                spread = Math.Max(spread, Math.Abs(AudioClock.FromQpc(qpcPosition) - expected));
            }
            else
            {
                badStamps++;
            }
            down.Add(floats, samples);
            frames += count;
            packets++;
        }
    }

    private void AddSilence(long missingFrames)
    {
        var zeros = new float[Math.Min(missingFrames, format.SampleRate) * format.Channels];
        while (missingFrames > 0)
        {
            var n = (int)Math.Min(missingFrames, zeros.Length / format.Channels);
            down.Add(zeros.AsSpan(0, n * format.Channels), samples);
            frames += n;
            missingFrames -= n;
        }
    }

    private static void Convert16(ReadOnlySpan<byte> buffer, Span<float> into)
    {
        var shorts = MemoryMarshal.Cast<byte, short>(buffer);
        for (var i = 0; i < into.Length; i++)
            into[i] = shorts[i] / 32768f;
    }
}
