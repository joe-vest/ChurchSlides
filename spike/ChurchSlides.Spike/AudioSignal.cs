namespace ChurchSlides.Spike;

/// <summary>
/// Mixes interleaved samples to mono and averages them down to 11,025 per second (C0.4 decision 3).
/// Each output sample is the mean of the input over exactly 1/11,025 s, so any input rate maps onto
/// the same timeline: output sample k covers input time k/11,025 to (k+1)/11,025.
/// </summary>
public sealed class MonoDownsampler
{
    public const int Rate = 11025;

    private readonly int channels;
    private readonly double step;
    private double filled;
    private double sum;

    public MonoDownsampler(int inputRate, int channels)
    {
        if (inputRate < Rate)
            throw new ArgumentOutOfRangeException(nameof(inputRate), inputRate, $"Input below {Rate} Hz.");
        this.channels = channels;
        step = inputRate / (double)Rate;
    }

    public void Add(ReadOnlySpan<float> interleaved, List<float> output)
    {
        for (var i = 0; i + channels <= interleaved.Length; i += channels)
        {
            float mono = 0;
            for (var c = 0; c < channels; c++)
                mono += interleaved[i + c];
            mono /= channels;

            var room = step - filled;
            if (room > 1)
            {
                sum += mono;
                filled += 1;
            }
            else
            {
                output.Add((float)((sum + mono * room) / step));
                var rest = 1 - room;
                sum = mono * rest;
                filled = rest;
            }
        }
    }
}

/// <summary>
/// The file time heard at each moment of a loopback recording: a 0.5 s window every 0.25 s,
/// each matched against the reference near where the one before says it should be (a wide
/// search can land on a repeat in the music). Shared by the playback (C0.4a) and Cue (C0.4b) runs.
/// </summary>
public sealed class HeardTimeline
{
    private const double HopSeconds = 0.25;

    private readonly Recording rec;
    private readonly List<(int Index, double FileSeconds)> points;

    private HeardTimeline(Recording rec, List<(int, double)> points, int windows, int steps)
    {
        this.rec = rec;
        this.points = points;
        Windows = windows;
        Steps = steps;
    }

    /// <summary>Windows tried.</summary>
    public int Windows { get; }

    /// <summary>Windows matched.</summary>
    public int Count => points.Count;

    /// <summary>Matches more than 5 ms from where the one before said (dropouts or glitches).</summary>
    public int Steps { get; }

    /// <summary>Builds the timeline over recording samples [onset, offset), starting at file time <paramref name="startFileSeconds"/>.</summary>
    public static HeardTimeline Build(Recording rec, Reference reference, int onset, int offset, double startFileSeconds, int window)
    {
        var points = new List<(int, double)>();
        var steps = 0;
        var windows = 0;
        var expected = startFileSeconds;
        var prev = onset;
        for (var i = onset; i + window <= offset; i += Reference.Index(HopSeconds))
        {
            windows++;
            var guess = expected + Reference.ToSeconds(i - prev);
            var m = Correlation.Find(rec.Samples.AsSpan(i, window), reference.Samples, Reference.Index(guess - 0.1), Reference.Index(guess + 0.1));
            if (!m.IsTrusted)
                continue;
            var at = Reference.ToSeconds(m.Index);
            if (points.Count > 0 && Math.Abs(at - guess) > 0.005)
                steps++;
            points.Add((i, at));
            expected = at;
            prev = i;
        }
        return new HeardTimeline(rec, points, windows, steps);
    }

    /// <summary>The file time heard at AudioClock time <paramref name="clockSeconds"/>: the nearest window before it, run on at 1×.</summary>
    public double FileSecondsAt(double clockSeconds)
    {
        if (points.Count == 0)
            return double.NaN;
        var index = rec.IndexAt(clockSeconds);
        var (i, file) = points[Last(p => p.Index <= index)];
        return file + Reference.ToSeconds(index - i);
    }

    /// <summary>The AudioClock time at which file time <paramref name="fileSeconds"/> was heard (the inverse of <see cref="FileSecondsAt"/>).</summary>
    public double ClockSecondsOf(double fileSeconds)
    {
        if (points.Count == 0)
            return double.NaN;
        var (i, file) = points[Last(p => p.FileSeconds <= fileSeconds)];
        return rec.TimeOf(i + (fileSeconds - file) * MonoDownsampler.Rate);
    }

    /// <summary>The last point that passes (points are in order on both axes), or the first if none does.</summary>
    private int Last(Func<(int Index, double FileSeconds), bool> atOrBefore)
    {
        var lo = 0;
        var hi = points.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (atOrBefore(points[mid]))
                lo = mid;
            else
                hi = mid - 1;
        }
        return lo;
    }
}

/// <summary>A match of a probe inside a longer signal: where it starts (fractional sample) and how well it matches (0..1).</summary>
public readonly record struct Match(double Index, double Score)
{
    /// <summary>Below this the match is a guess (silence, or a part of the song that repeats).</summary>
    public const double Trusted = 0.6;

    public bool IsTrusted => Score >= Trusted;
}

/// <summary>Normalized cross-correlation, coarse (8× fewer samples, best 5 peaks) then fine.</summary>
public static class Correlation
{
    private const int Coarse = 8;
    private const int Peaks = 5;

    /// <summary>Finds where <paramref name="probe"/> best matches <paramref name="haystack"/>, starting at an index in [from, to].</summary>
    public static Match Find(ReadOnlySpan<float> probe, ReadOnlySpan<float> haystack, int from, int to)
    {
        from = Math.Max(0, from);
        to = Math.Min(to, haystack.Length - probe.Length);
        if (probe.Length < Coarse * 8 || to < from)
            return new Match(double.NaN, 0);

        // Coarse: both signals averaged over 8 samples, every lag in range; keep the best 3 peaks.
        var p = Decimate(probe);
        var h = Decimate(haystack.Slice(from, to - from + probe.Length));
        var lags = h.Length - p.Length + 1;
        var coarse = new double[Math.Max(lags, 0)];
        for (var lag = 0; lag < lags; lag++)
            coarse[lag] = Score(p, h.AsSpan(lag, p.Length));
        var peaks = Enumerable.Range(0, lags)
            .Where(i => (i == 0 || coarse[i] >= coarse[i - 1]) && (i == lags - 1 || coarse[i] >= coarse[i + 1]))
            .OrderByDescending(i => coarse[i])
            .Take(Peaks);

        // Fine: every sample around each coarse peak (averaging blurs a sharp peak, so look wide),
        // then a parabola through the best and its neighbours.
        var best = new Match(double.NaN, double.NegativeInfinity);
        foreach (var peak in peaks)
        {
            var centre = from + peak * Coarse;
            var lo = Math.Max(from, centre - Coarse * 6);
            var hi = Math.Min(to, centre + Coarse * 6);
            for (var i = lo; i <= hi; i++)
            {
                var s = Score(probe, haystack.Slice(i, probe.Length));
                if (s > best.Score)
                    best = new Match(i, s);
            }
        }
        if (double.IsNaN(best.Index))
            return new Match(double.NaN, 0);

        var at = (int)best.Index;
        if (at > from && at < to)
        {
            var left = Score(probe, haystack.Slice(at - 1, probe.Length));
            var right = Score(probe, haystack.Slice(at + 1, probe.Length));
            var curve = left - 2 * best.Score + right;
            if (curve < 0)
                return new Match(at + 0.5 * (left - right) / curve, best.Score);
        }
        return best;
    }

    private static double Score(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double ab = 0, aa = 0, bb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            ab += a[i] * b[i];
            aa += a[i] * a[i];
            bb += b[i] * b[i];
        }
        return aa > 0 && bb > 0 ? ab / Math.Sqrt(aa * bb) : 0;
    }

    private static float[] Decimate(ReadOnlySpan<float> x)
    {
        var result = new float[x.Length / Coarse];
        for (var i = 0; i < result.Length; i++)
        {
            float sum = 0;
            for (var j = 0; j < Coarse; j++)
                sum += x[i * Coarse + j];
            result[i] = sum / Coarse;
        }
        return result;
    }
}
