using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace ChurchSlides.Spike;

/// <summary>
/// The automatic part of S4 (C0.4a): file facts, seek accuracy per reader, the decoder offset between
/// readers, and for each reader and output a trimmed playback heard through loopback (start, end and
/// position against what was heard), seek-and-play delays, and memory and CPU while playing.
/// </summary>
public sealed class AudioTests(IReadOnlyList<string> files, Metrics metrics, Action<string> status)
{
    private const double ProbeSeconds = 0.5;
    private const double SearchSeconds = 1.0;
    private const double HopSeconds = 0.25;
    private const double PlaySeconds = 20;
    private const int SeekPlays = 5;
    private const int RandomSeeks = 7;
    private const double SeekPassMs = 20; // S4-a
    private const double PositionP95PassMs = 20; // S4-b
    private const double PositionMaxPassMs = 40; // S4-b
    private const double TrimEndPassMs = 20; // S4-b
    private const double MemoryPassMb = 20; // S4-e

    private const string Signed = "+0.0;-0.0;0.0";

    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    /// <summary>Worst seek error per reader over the VBR files (or all files if none is VBR).</summary>
    private readonly Dictionary<ReaderKind, double> worstSeekMs = [];

    public async Task<List<Figure>> Run()
    {
        if (files.Count == 0)
            return [new("Not run", "no MP3 files: put a few of the church's songs in the media folder (top right)")];

        var figures = new List<Figure>();
        var facts = new List<(string Path, Mp3Facts Facts)>();
        foreach (var path in files)
        {
            status($"S4: reading {Path.GetFileName(path)}…");
            var (fact, line) = await Task.Run(() =>
            {
                var f = AudioFiles.Inspect(path);
                var opens = Enum.GetValues<ReaderKind>().Select(r => $"{r}: {AudioFiles.OpenFacts(path, r)}");
                return (f, $"{new FileInfo(path).Length / 1024} KB, {f}. {string.Join("; ", opens)}");
            });
            facts.Add((path, fact));
            figures.Add(new($"File {Path.GetFileName(path)}", line));
        }

        var anyVbr = facts.Any(f => f.Facts.Vbr);
        foreach (var (path, fact) in facts)
        {
            status($"S4: seeks in {Path.GetFileName(path)}…");
            var counts = !anyVbr || fact.Vbr;
            figures.AddRange(await Task.Run(() => Seeks(path, counts)));
        }
        figures.Add(SeekVerdict(anyVbr));

        var song = facts.FirstOrDefault(f => f.Facts.Vbr).Path ?? files[0];
        figures.Add(new("Playback song", $"{Path.GetFileName(song)} ({(anyVbr ? "the first VBR file" : "no VBR file found, the first file")}), {PlaySeconds:F0} s from Trim Start, then {SeekPlays} seek-and-plays"));
        var runs = new List<PlaybackRun>();
        foreach (var combo in AudioCombo.All)
        {
            status($"S4: playing with {combo}…");
            try
            {
                var run = await Playback(song, combo);
                runs.Add(run);
                figures.AddRange(run.Figures);
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or NAudio.MmException or NotSupportedException)
            {
                figures.Add(new($"{combo}", $"failed: {ex.GetType().Name}: {ex.Message}"));
            }
        }
        figures.AddRange(PlaybackVerdicts(runs));
        return figures;
    }

    // ---- Seeks and decoder offset ----

    /// <summary>Seeks to 12 positions with each reader and finds where each really landed (decision 3).</summary>
    private List<Figure> Seeks(string path, bool countsForVerdict)
    {
        var name = Path.GetFileName(path);
        var figures = new List<Figure>();
        var references = new Dictionary<ReaderKind, Reference>();
        foreach (var kind in Enum.GetValues<ReaderKind>())
        {
            var reference = AudioFiles.Decode(path, kind);
            references[kind] = reference;
            var last = reference.Seconds - ProbeSeconds - 0.1;
            var random = new Random(name.Length * 31 + 7);
            var targets = new List<double> { 0.5, 3, 30, 120, last - 1.5 };
            for (var i = 0; i < RandomSeeks; i++)
                targets.Add(0.5 + random.NextDouble() * (last - 0.5));
            targets = targets.Where(t => t >= 0.5 && t <= last).ToList();

            using var reader = AudioFiles.Open(path, kind);
            var skipper = new StallSkipper(reader);
            var sampleProvider = skipper.ToSampleProvider();
            var errors = new List<(double Target, double ErrorMs)>();
            var times = new List<double>();
            var stalls = new List<double>();
            var untrusted = 0;
            foreach (var target in targets)
            {
                var clock = Stopwatch.StartNew();
                reader.CurrentTime = TimeSpan.FromSeconds(target);
                skipper.StallMs = 0;
                var probe = new List<float>();
                AudioFiles.ReadMono(sampleProvider, probe, TimeSpan.FromSeconds(ProbeSeconds));
                times.Add(clock.Elapsed.TotalMilliseconds);
                if (skipper.StallMs > 0)
                    stalls.Add(skipper.StallMs);
                var match = Correlation.Find(probe.ToArray(), reference.Samples, Reference.Index(target - SearchSeconds), Reference.Index(target + SearchSeconds));
                if (match.IsTrusted)
                    errors.Add((target, (Reference.ToSeconds(match.Index) - target) * 1000));
                else
                    untrusted++;
            }

            var worst = errors.Count == 0 ? double.NaN : errors.Max(e => Math.Abs(e.ErrorMs));
            if (countsForVerdict && errors.Count > 0)
                worstSeekMs[kind] = Math.Max(worstSeekMs.GetValueOrDefault(kind), worst);
            var list = string.Join(", ", errors.Select(e => string.Create(C, $"{e.Target:F1} s {e.ErrorMs.ToString(Signed, C)}")));
            var stalled = stalls.Count == 0 ? "no seek read made-up silence"
                : string.Create(C, $"after {stalls.Count} of {targets.Count} seeks the reader returned silence without moving for {Summary.Of(stalls)} of wall time (skipped here; see the seek-and-play figures)");
            figures.Add(new($"Seek {name}, {kind}", errors.Count == 0
                ? $"no trusted match in {targets.Count} seeks (silence?); {stalled}"
                : string.Create(C,
                    $"error mean {errors.Average(e => Math.Abs(e.ErrorMs)):F1} ms, max {worst:F1} ms over {errors.Count} seeks{(untrusted > 0 ? $" ({untrusted} untrusted, left out)" : "")}; seek and read 0.5 s {Summary.Of(times)}; {stalled}; whole-file decode {reference.DecodeMs:F0} ms. Each: {list} ms")));
        }

        figures.Add(new($"Decoder offset {name}", DecoderOffset(references[ReaderKind.Mp3FileReader], references[ReaderKind.MediaFoundationReader])));
        return figures;
    }

    /// <summary>
    /// NAudio 3.3's Mp3FileReader, read straight after some seeks, returns zeros without moving its
    /// position for tens of milliseconds of wall time, then the right audio. This waits those reads
    /// out (up to 2 s), so seek accuracy is measured on the real audio, and records how long it took.
    /// </summary>
    private sealed class StallSkipper(WaveStream reader) : IWaveProvider
    {
        public double StallMs { get; set; }
        public WaveFormat WaveFormat => reader.WaveFormat;

        public int Read(Span<byte> buffer)
        {
            Stopwatch? stalled = null;
            while (true)
            {
                var before = reader.Position;
                var read = reader.Read(buffer);
                var made = read > 0 && reader.Position == before && buffer[..read].IndexOfAnyExcept((byte)0) < 0;
                if (!made || stalled?.Elapsed > TimeSpan.FromSeconds(2))
                {
                    if (stalled is not null)
                        StallMs += stalled.Elapsed.TotalMilliseconds;
                    return read;
                }
                stalled ??= Stopwatch.StartNew();
                Thread.Yield();
            }
        }
    }

    /// <summary>Where MediaFoundationReader's decode sits against Mp3FileReader's (encoder delay handling, AUD-005).</summary>
    private static string DecoderOffset(Reference mp3, Reference mf)
    {
        var length = Math.Min(mp3.Seconds, mf.Seconds);
        var offsets = new List<string>();
        foreach (var at in new[] { 0.1, 0.5, 0.9 }.Select(f => f * length))
        {
            var probe = mf.Samples.AsSpan(Reference.Index(at), Reference.Index(ProbeSeconds));
            var match = Correlation.Find(probe, mp3.Samples, Reference.Index(at - SearchSeconds), Reference.Index(at + SearchSeconds));
            offsets.Add(match.IsTrusted
                ? string.Create(C, $"{(Reference.ToSeconds(match.Index) - at) * 1000:+0.0;-0.0;0.0} ms at {at:F0} s")
                : string.Create(C, $"no match at {at:F0} s"));
        }
        return "MediaFoundationReader file time minus Mp3FileReader file time for the same sound: " + string.Join(", ", offsets) +
            ". Cues are stored as file time, so changing reader later would move every Cue by this.";
    }

    private Figure SeekVerdict(bool anyVbr)
    {
        var best = worstSeekMs.OrderBy(w => w.Value).FirstOrDefault();
        var on = anyVbr ? "the VBR files" : "the files (none is VBR)";
        if (worstSeekMs.Count == 0)
            return new("S4-a: seek error ≤ 20 ms on VBR files, one reader", "no trusted seeks");
        var each = string.Join(", ", worstSeekMs.Select(w => string.Create(C, $"{w.Key} worst {w.Value:F1} ms")));
        return new($"S4-a: seek error ≤ {SeekPassMs:F0} ms on VBR files, one reader",
            $"{(best.Value <= SeekPassMs ? "pass" : "FAIL")} on {on}: {each}");
    }

    // ---- Playback through loopback ----

    private sealed record PlaybackRun(AudioCombo Combo, Summary OutputResidual, double OutputOffsetMs, Summary ReaderResidual, double ReaderOffsetMs, double TrimEndMs, double AddedMb, List<Figure> Figures);

    private readonly record struct PositionSample(double At, double Output, double Reader);

    /// <summary>A heard moment: loopback sample index and the file time heard there.</summary>
    private readonly record struct Heard(int Index, double FileSeconds);

    private async Task<PlaybackRun> Playback(string song, AudioCombo combo)
    {
        var label = combo.ToString();
        var reference = await Task.Run(() => AudioFiles.Decode(song, combo.Reader));
        await Settle();
        var idleMb = metrics.Latest.WorkingSetMb;

        using var probe = LoopbackProbe.Start();
        using var player = new SpikePlayer(song, combo);
        var trimStart = Math.Min(10, player.Length.TotalSeconds * 0.1);
        var trimEnd = Math.Min(trimStart + PlaySeconds, player.Length.TotalSeconds - 1);
        await Task.Delay(500);

        var positions = new List<PositionSample>();
        using var sampling = new CancellationTokenSource();
        var sampler = new Thread(() => Sample(player, positions, sampling.Token)) { IsBackground = true, Name = "Position sampler" };

        var ended = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
        player.Stopped += _ => ended.TrySetResult(AudioClock.Seconds);
        var from = metrics.Now;
        var playAt = AudioClock.Seconds;
        await player.PlayFrom(trimStart, trimEnd);
        sampler.Start();
        await Task.WhenAny(ended.Task, Task.Delay(TimeSpan.FromSeconds(PlaySeconds + 10)));
        var playing = metrics.Summarize(from, metrics.Now);
        var stoppedAt = ended.Task.IsCompleted ? ended.Task.Result : double.NaN;
        await Task.Delay(600);

        var random = new Random(7);
        var commands = new List<(double At, double Target)>();
        for (var i = 0; i < SeekPlays; i++)
        {
            var target = trimStart + random.NextDouble() * Math.Max(1, player.Length.TotalSeconds - trimStart - 10);
            var at = AudioClock.Seconds;
            await player.PlayFrom(target, null);
            commands.Add((at, target));
            await Task.Delay(2500);
        }
        sampling.Cancel();
        sampler.Join();
        await player.StopAsync();
        await Task.Delay(300);
        var recording = probe.Stop();

        var figures = new List<Figure>();
        if (recording.IsSilent)
        {
            figures.Add(new($"{label}", $"loopback heard nothing ({probe.Format}). Is the volume muted? Keep it low but not muted."));
            return new PlaybackRun(combo, default, double.NaN, default, double.NaN, double.NaN, double.NaN, figures);
        }
        return await Task.Run(() => Analyse(label, combo, reference, recording, positions, playAt, stoppedAt, trimStart, trimEnd, commands, playing, idleMb));
    }

    private static void Sample(SpikePlayer player, List<PositionSample> into, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            var before = AudioClock.Seconds;
            var output = player.SafeSeconds();
            var reader = player.ReaderSeconds;
            var after = AudioClock.Seconds;
            lock (into)
                into.Add(new PositionSample((before + after) / 2, output, reader));
            Thread.Sleep(5);
        }
    }

    private static PlaybackRun Analyse(
        string label, AudioCombo combo, Reference reference, Recording rec, List<PositionSample> positions, double playAt, double stoppedAt,
        double trimStart, double trimEnd, List<(double At, double Target)> commands, MetricSummary playing, double idleMb)
    {
        var figures = new List<Figure>();
        var window = Reference.Index(ProbeSeconds);
        var onset = rec.FirstSound(rec.IndexAt(playAt), rec.Samples.Length);
        var offset = rec.LastSound(onset, rec.IndexAt(commands[0].At));
        if (onset < 0 || offset - onset < 2 * window)
        {
            figures.Add(new($"{label}", "the trimmed playback wasn't heard through loopback"));
            return new PlaybackRun(combo, default, double.NaN, default, double.NaN, double.NaN, double.NaN, figures);
        }

        // Start: the file time of the first sound heard.
        var first = Correlation.Find(rec.Samples.AsSpan(onset, window), reference.Samples, Reference.Index(trimStart - SearchSeconds), Reference.Index(trimStart + SearchSeconds));

        // The heard timeline: a window every 0.25 s, each searched near where the last one says it should be.
        var heard = new List<Heard>();
        var steps = 0;
        var expected = Reference.ToSeconds(first.Index);
        var prev = onset;
        var windows = 0;
        for (var i = onset; i + window <= offset; i += Reference.Index(HopSeconds))
        {
            windows++;
            var guess = expected + Reference.ToSeconds(i - prev);
            var m = Correlation.Find(rec.Samples.AsSpan(i, window), reference.Samples, Reference.Index(guess - 0.1), Reference.Index(guess + 0.1));
            if (!m.IsTrusted)
                continue;
            var at = Reference.ToSeconds(m.Index);
            if (heard.Count > 0 && Math.Abs(at - guess) > 0.005)
                steps++;
            heard.Add(new Heard(i, at));
            expected = at;
            prev = i;
        }
        // End: the last 0.5 s heard, searched near where the timeline says it is (a wide search can
        // land on a repeat in the music), so its end is the file time of the last sound.
        var lastFrom = offset + 1 - window;
        var lastGuess = HeardAt(rec, heard, rec.TimeOf(lastFrom));
        var last = double.IsNaN(lastGuess) ? new Match(double.NaN, 0)
            : Correlation.Find(rec.Samples.AsSpan(lastFrom, window), reference.Samples, Reference.Index(lastGuess - 0.1), Reference.Index(lastGuess + 0.1));
        var firstHeardMs = (Reference.ToSeconds(first.Index) - trimStart) * 1000;
        var lastHeardMs = (Reference.ToSeconds(last.Index) + ProbeSeconds - trimEnd) * 1000;
        figures.Add(new($"{label}: start and end", string.Create(C,
            $"first sound {(rec.TimeOf(onset) - playAt) * 1000:F0} ms after Play; first heard {firstHeardMs.ToString(Signed, C)} ms from Trim Start{Unsure(first)}; " +
            $"last heard {lastHeardMs.ToString(Signed, C)} ms from Trim End{Unsure(last)}; stop event {(stoppedAt - rec.TimeOf(offset)) * 1000:F0} ms after the last sound")));
        figures.Add(new($"{label}: heard timeline", string.Create(C,
            $"{heard.Count} of {windows} windows matched; jumps over 5 ms (dropouts or glitches): {steps}; loopback {rec.Packets} packets, {rec.Gaps} gaps, {rec.BadStamps} without a timestamp, device clock against QPC within {rec.ClockSpreadMs:F1} ms")));

        // Reported position minus heard position, sampled every ~5 ms (plus the timer's resolution).
        var inside = positions.Where(p => p.At > rec.TimeOf(onset) + 0.3 && p.At < rec.TimeOf(offset) - 0.3).ToList();
        var outputErrors = inside.Select(p => (p.Output - HeardAt(rec, heard, p.At)) * 1000).Where(double.IsFinite).ToList();
        var readerErrors = inside.Select(p => (p.Reader - HeardAt(rec, heard, p.At)) * 1000).Where(double.IsFinite).ToList();
        var (outputOffset, outputResidual) = Residual(outputErrors);
        var (readerOffset, readerResidual) = Residual(readerErrors);
        figures.Add(new($"{label}: output position − heard", Describe(outputOffset, outputResidual)));
        figures.Add(new($"{label}: reader position − heard", Describe(readerOffset, readerResidual)));

        // Seek-and-play: from the command to where the target's sound first shows in the loopback, then
        // the output position against what is heard from there on (less the offset above): a reader
        // that hands the output made-up silence after a seek leaves the position ahead of the sound.
        var delays = new List<double>();
        var after = new List<string>();
        for (var c = 0; c < commands.Count; c++)
        {
            var (at, target) = commands[c];
            var snippet = reference.Samples.AsSpan(Reference.Index(target), Reference.Index(0.4));
            var from = rec.IndexAt(at);
            var m = Correlation.Find(snippet, rec.Samples, from, from + Reference.Index(1.5));
            if (!m.IsTrusted)
                continue;
            var heardFrom = rec.TimeOf(m.Index);
            delays.Add((heardFrom - at) * 1000);
            var until = c + 1 < commands.Count ? commands[c + 1].At : double.PositiveInfinity;
            var errors = positions
                .Where(p => p.At > heardFrom + 0.3 && p.At < until - 0.2)
                .Select(p => (p.Output - (target + p.At - heardFrom)) * 1000 - outputOffset)
                .ToList();
            if (errors.Count > 0)
                after.Add(errors.Average().ToString(Signed, C));
        }
        figures.Add(new($"{label}: seek and play, command to first heard (AUD-010)", $"{Summary.Of(delays)}{(delays.Count < commands.Count ? $", {commands.Count - delays.Count} not found" : "")}"));
        figures.Add(new($"{label}: after seek and play, output position − heard, less the offset", after.Count == 0 ? "none measured" : $"{string.Join(", ", after)} ms (one per seek)"));

        var added = playing.WorkingSetMaxMb - idleMb;
        figures.Add(new($"{label}: memory and CPU while playing", string.Create(C,
            $"working set +{added:F1} MB over idle ({idleMb:F0} MB, after full GC), CPU avg {playing.CpuAvgPercent:F1}% max {playing.CpuMaxPercent:F1}%")));

        return new PlaybackRun(combo, outputResidual, outputOffset, readerResidual, readerOffset, lastHeardMs, added, figures);
    }

    /// <summary>The file time heard at AudioClock time <paramref name="at"/>: the nearest heard window, run on at 1×.</summary>
    private static double HeardAt(Recording rec, List<Heard> heard, double at)
    {
        if (heard.Count == 0)
            return double.NaN;
        var index = rec.IndexAt(at);
        var lo = 0;
        var hi = heard.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (heard[mid].Index <= index)
                lo = mid;
            else
                hi = mid - 1;
        }
        var h = heard[lo];
        return h.FileSeconds + Reference.ToSeconds(index - h.Index);
    }

    /// <summary>The mean (the latency offset to subtract) and what's left after subtracting it.</summary>
    private static (double Offset, Summary Residual) Residual(List<double> errors)
    {
        if (errors.Count == 0)
            return (double.NaN, default);
        var mean = errors.Average();
        return (mean, Summary.Of(errors.Select(e => Math.Abs(e - mean))));
    }

    private static string Describe(double offset, Summary residual) => residual.Count == 0
        ? "no samples"
        : string.Create(C, $"offset (mean) {offset:+0.0;-0.0;0.0} ms; left after subtracting it: {residual}");

    private static string Unsure(Match m) => m.IsTrusted ? "" : string.Create(C, $" (weak match {m.Score:F2})");

    private static IEnumerable<Figure> PlaybackVerdicts(List<PlaybackRun> runs)
    {
        var heard = runs.Where(r => r.OutputResidual.Count > 0).ToList();
        if (heard.Count == 0)
        {
            yield return new("S4-b", "no playback was heard through loopback");
            yield break;
        }
        var candidates = heard
            .SelectMany(r => new[] { (r, Source: "output", Residual: r.OutputResidual, Offset: r.OutputOffsetMs), (r, Source: "reader", Residual: r.ReaderResidual, Offset: r.ReaderOffsetMs) })
            .Where(c => c.Residual.Count > 0)
            .OrderBy(c => c.Residual.P95)
            .ToList();
        var best = candidates[0];
        var pass = best.Residual.P95 <= PositionP95PassMs && best.Residual.Max <= PositionMaxPassMs;
        yield return new($"S4-b: position minus a fixed offset within {PositionP95PassMs:F0} ms p95, {PositionMaxPassMs:F0} ms max",
            string.Create(C, $"{(pass ? "pass" : "FAIL")}: best {best.r.Combo}, {best.Source} position, offset {best.Offset:+0.0;-0.0;0.0} ms, p95 {best.Residual.P95:F1} ms, max {best.Residual.Max:F1} ms"));
        yield return new($"S4-b: Trim End stops within {TrimEndPassMs:F0} ms",
            string.Join("; ", heard.Select(r => string.Create(C, $"{r.Combo} {r.TrimEndMs:+0.0;-0.0;0.0} ms{(Math.Abs(r.TrimEndMs) <= TrimEndPassMs ? "" : " FAIL")}"))));
        var worstMb = heard.Max(r => r.AddedMb);
        yield return new($"S4-e: playing adds ≤ {MemoryPassMb:F0} MB working set",
            string.Create(C, $"{(worstMb <= MemoryPassMb ? "pass" : "FAIL")} (worst +{worstMb:F1} MB)"));
    }

    private async Task Settle()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect();
        await Task.Delay(Metrics.Interval * 3);
    }
}
