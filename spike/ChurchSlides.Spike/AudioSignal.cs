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
