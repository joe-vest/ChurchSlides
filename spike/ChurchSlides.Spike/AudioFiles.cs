using System.Diagnostics;
using System.Globalization;
using System.Text;
using NAudio.Wave;

namespace ChurchSlides.Spike;

/// <summary>The two MP3 readers compared (C0.4 decision 4).</summary>
public enum ReaderKind
{
    Mp3FileReader,
    MediaFoundationReader,
}

/// <summary>What the frame headers say about an MP3, read without decoding.</summary>
public sealed record Mp3Facts(int Frames, int MinKbps, int MaxKbps, int SampleRate, string InfoHeader, TimeSpan FrameLength)
{
    /// <summary>Variable bit rate: the frames' bit rates differ.</summary>
    public bool Vbr => MinKbps != MaxKbps;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{(Vbr ? "VBR" : "CBR")} {(Vbr ? $"{MinKbps}-{MaxKbps}" : MinKbps.ToString(CultureInfo.InvariantCulture))} kbps, {SampleRate} Hz, {Frames} frames ({FrameLength.TotalSeconds:F2} s), {InfoHeader}");
}

/// <summary>One file decoded once from the start by one reader, as bounded mono at 11,025 per second (decision 3).</summary>
public sealed class Reference(float[] samples, double decodeMs)
{
    /// <summary>Longest stretch kept (about 40 MB of floats at most).</summary>
    public static readonly TimeSpan Max = TimeSpan.FromMinutes(15);

    public float[] Samples { get; } = samples;
    public double DecodeMs { get; } = decodeMs;
    public double Seconds => Samples.Length / (double)MonoDownsampler.Rate;

    public static int Index(double seconds) => (int)Math.Round(seconds * MonoDownsampler.Rate);

    public static double ToSeconds(double index) => index / MonoDownsampler.Rate;
}

/// <summary>The MP3s in the media folder, the readers, and the facts and references the S4 run needs.</summary>
public static class AudioFiles
{
    public static IReadOnlyList<string> Scan(string? folder) =>
        folder is null || !Directory.Exists(folder)
            ? []
            : Directory.EnumerateFiles(folder, "*.mp3").Order(StringComparer.OrdinalIgnoreCase).ToList();

    public static WaveStream Open(string path, ReaderKind kind) => kind switch
    {
        ReaderKind.Mp3FileReader => new Mp3FileReader(path),
        _ => new MediaFoundationReader(path),
    };

    /// <summary>Walks the frame headers: bit rates (VBR or CBR), frame count, and the Xing/Info/VBRI header if any.</summary>
    public static Mp3Facts Inspect(string path)
    {
        using var stream = File.OpenRead(path);
        Id3v2Tag.ReadTag(stream);
        var first = Mp3Frame.LoadFromStream(stream, true);
        if (first is null)
            return new Mp3Facts(0, 0, 0, 0, "no MP3 frames found", TimeSpan.Zero);

        var header = InfoHeader(first);
        int frames = 1, samples = first.SampleCount, min = first.BitRate, max = first.BitRate;
        if (header != "no Xing/Info header")
        {
            // The header frame is silent and its bit rate says nothing about the music.
            frames = samples = 0;
            min = int.MaxValue;
            max = 0;
        }
        while (Mp3Frame.LoadFromStream(stream, false) is { } frame)
        {
            frames++;
            samples += frame.SampleCount;
            min = Math.Min(min, frame.BitRate);
            max = Math.Max(max, frame.BitRate);
        }
        return new Mp3Facts(frames, min / 1000, max / 1000, first.SampleRate, header, TimeSpan.FromSeconds(samples / (double)first.SampleRate));
    }

    /// <summary>Opens the file with the reader and times it. Mp3FileReader 3.x indexes lazily unless the length is unknown.</summary>
    public static string OpenFacts(string path, ReaderKind kind)
    {
        var clock = Stopwatch.StartNew();
        using var reader = Open(path, kind);
        var openMs = clock.Elapsed.TotalMilliseconds;
        var text = string.Create(CultureInfo.InvariantCulture, $"open {openMs:F0} ms, length {reader.TotalTime:m\\:ss\\.fff}");
        if (reader is Mp3FileReader mp3)
        {
            if (mp3.IsLengthExact)
            {
                text += ", length exact at open";
            }
            else
            {
                clock.Restart();
                mp3.EnsureExactLengthAsync(CancellationToken.None).GetAwaiter().GetResult();
                text += string.Create(CultureInfo.InvariantCulture, $", length estimated at open, exact after a {clock.Elapsed.TotalMilliseconds:F0} ms scan: {mp3.TotalTime:m\\:ss\\.fff}");
            }
        }
        return text;
    }

    /// <summary>Decodes from the start with the reader (streamed, never whole) into the bounded mono reference.</summary>
    public static Reference Decode(string path, ReaderKind kind)
    {
        var clock = Stopwatch.StartNew();
        using var reader = Open(path, kind);
        var samples = new List<float>((int)Math.Min(reader.TotalTime.TotalSeconds, Reference.Max.TotalSeconds) * MonoDownsampler.Rate + MonoDownsampler.Rate);
        ReadMono(reader.ToSampleProvider(), samples, Reference.Max);
        return new Reference([.. samples], clock.Elapsed.TotalMilliseconds);
    }

    /// <summary>Reads up to <paramref name="length"/> from the provider into mono at 11,025 per second.</summary>
    public static void ReadMono(ISampleProvider source, List<float> output, TimeSpan length)
    {
        var format = source.WaveFormat;
        var down = new MonoDownsampler(format.SampleRate, format.Channels);
        var buffer = new float[format.SampleRate * format.Channels / 10];
        var left = (long)(length.TotalSeconds * format.SampleRate) * format.Channels;
        while (left > 0)
        {
            var read = source.Read(buffer.AsSpan(0, (int)Math.Min(buffer.Length, left)));
            if (read == 0)
                break;
            down.Add(buffer.AsSpan(0, read), output);
            left -= read;
        }
    }

    private static string InfoHeader(Mp3Frame frame)
    {
        var data = frame.RawData;
        if (data is null)
            return "no Xing/Info header";
        var text = Encoding.ASCII.GetString(data);
        foreach (var (tag, meaning) in new[] { ("Xing", "Xing header (VBR)"), ("Info", "Info header (CBR)"), ("VBRI", "VBRI header (Fraunhofer VBR)") })
        {
            if (text.Contains(tag, StringComparison.Ordinal))
                return meaning;
        }
        return "no Xing/Info header";
    }
}
