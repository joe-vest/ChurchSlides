using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ChurchSlides.Spike;

public sealed record Figure(string Name, string Value);

public sealed record CheckItem(string Id, string Section, string Text);

public sealed class CheckAnswer
{
    public string Verdict { get; set; } = "";
    public string Note { get; set; } = "";
}

public sealed class FigureSection
{
    public DateTimeOffset MeasuredAt { get; set; }
    public List<Figure> Figures { get; set; } = [];
}

/// <summary>Everything kept between launches, in spike-state.json next to the program.</summary>
public sealed class SpikeState
{
    public string? MediaFolder { get; set; }
    public Dictionary<string, FigureSection> Sections { get; set; } = [];
    public List<StartupRun> StartupRuns { get; set; } = [];
    public Dictionary<string, CheckAnswer> Checks { get; set; } = [];
}

/// <summary>
/// Figures from the automatic run plus the eye checklist. Saved as spike-state.json (so later
/// launches add to it) and as spike-results-&lt;date&gt;.md (the file to send back).
/// </summary>
public sealed class Results
{
    public const string S1 = "S1 Output window";
    public const string S2 = "S2 Slides and crossfade";
    public const string S6 = "S6 Memory and decode";
    public const string S7 = "S7 Startup";
    public const string S8 = "S8 System";

    /// <summary>Section order in the results file. Later chunks add their sections here.</summary>
    public static readonly string[] SectionOrder = [S8, S1, S2, S6, S7];

    public static readonly CheckItem[] Checklist =
    [
        new("S1-1", S1, "On the Output monitor, all four edge lines are visible: red top, green right, blue bottom, yellow left."),
        new("S1-2", S1, "On the projector (through the VGA splitter), all four edge lines are visible."),
        new("S1-3", S1, "No taskbar or other window shows over the test pattern."),
        new("S1-4", S1, "No white flash or flicker when the test pattern opens or closes."),
        new("S2-1", S2, "Crossfade at 300 ms looks smooth on the Output monitor, with pre-render on the render thread."),
        new("S2-2", S2, "Crossfade at 300 ms looks smooth with pre-render on the UI thread (note any stutter)."),
        new("S2-3", S2, "Crossfade looks smooth on the projector too."),
        new("S2-4", S2, "Slide text is sharp and easy to read on the projector."),
        new("S2-5", S2, "On the 4:3 image: Fit shows black bars, Fill crops without distortion, Stretch distorts."),
        new("S2-6", S2, "Right and Left (Cut) change the slide with no noticeable delay."),
        new("S2-7", S2, "Black (B), Clear (C) and Logo (L) show and hide at once. Clear keeps the background."),
        new("S7-1", S7, "Startup time is acceptable (see the Startup tab for cold and warm figures)."),
    ];

    private const int MaxStartupRuns = 30;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public Results(string folder)
    {
        Folder = folder;
        State = Load(StatePath);
        foreach (var item in Checklist)
        {
            if (!State.Checks.ContainsKey(item.Id))
                State.Checks[item.Id] = new CheckAnswer();
        }
    }

    public string Folder { get; }
    public SpikeState State { get; }
    private string StatePath => Path.Combine(Folder, "spike-state.json");

    public void SetFigures(string section, IEnumerable<Figure> figures) =>
        State.Sections[section] = new FigureSection { MeasuredAt = DateTimeOffset.Now, Figures = figures.ToList() };

    public void AddStartupRun(StartupRun run)
    {
        State.StartupRuns.Add(run);
        if (State.StartupRuns.Count > MaxStartupRuns)
            State.StartupRuns.RemoveAt(0);
    }

    /// <summary>Writes the state file and the dated results file. Returns the results file path.</summary>
    public string Save()
    {
        File.WriteAllText(StatePath, JsonSerializer.Serialize(State, Json));
        var path = Path.Combine(Folder, $"spike-results-{DateTime.Now:yyyy-MM-dd}.md");
        File.WriteAllText(path, ToMarkdown());
        return path;
    }

    public string ToMarkdown()
    {
        var md = new StringBuilder();
        md.AppendLine("# ChurchSlides spike results");
        md.AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"Written {DateTimeOffset.Now:yyyy-MM-dd HH:mm zzz} by the {Startup.BuildFlavor()} build.");

        foreach (var name in SectionOrder)
        {
            md.AppendLine();
            md.AppendLine(CultureInfo.InvariantCulture, $"## {name}");
            md.AppendLine();
            if (name == S7)
                AppendStartupRuns(md);
            if (State.Sections.TryGetValue(name, out var section))
            {
                md.AppendLine(CultureInfo.InvariantCulture, $"Measured {section.MeasuredAt:yyyy-MM-dd HH:mm}.");
                md.AppendLine();
                md.AppendLine("| Figure | Value |");
                md.AppendLine("|---|---|");
                foreach (var f in section.Figures)
                    md.AppendLine(CultureInfo.InvariantCulture, $"| {Cell(f.Name)} | {Cell(f.Value)} |");
            }
            else if (name != S7)
            {
                md.AppendLine("Not measured yet.");
            }
        }

        md.AppendLine();
        md.AppendLine("## Checklist");
        md.AppendLine();
        md.AppendLine("| Id | Check | Result | Note |");
        md.AppendLine("|---|---|---|---|");
        foreach (var item in Checklist)
        {
            var answer = State.Checks[item.Id];
            var verdict = answer.Verdict.Length == 0 ? "not checked" : answer.Verdict;
            md.AppendLine(CultureInfo.InvariantCulture, $"| {item.Id} | {Cell(item.Text)} | {verdict} | {Cell(answer.Note)} |");
        }
        return md.ToString();
    }

    private void AppendStartupRuns(StringBuilder md)
    {
        if (State.StartupRuns.Count == 0)
        {
            md.AppendLine("No launches recorded.");
            md.AppendLine();
            return;
        }
        md.AppendLine("Uptime is minutes since Windows started: a few minutes means a cold start after a reboot.");
        md.AppendLine();
        md.AppendLine("| When | Build | Kestrel | Uptime (min) | To Main (ms) | Kestrel start (ms) | To first frame (ms) | JIT methods | JIT time (ms) |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var r in State.StartupRuns)
        {
            md.AppendLine(CultureInfo.InvariantCulture,
                $"| {r.At:yyyy-MM-dd HH:mm} | {r.Build} | {(r.Kestrel ? "yes" : "no")} | {r.UptimeMinutes:F0} | {r.ToMainMs:F0} | {(r.Kestrel ? r.KestrelMs.ToString("F0", CultureInfo.InvariantCulture) : "")} | {r.ToFirstFrameMs:F0} | {r.JitMethods} | {r.JitMs:F0} |");
        }
        md.AppendLine();
    }

    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");

    private static SpikeState Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<SpikeState>(File.ReadAllText(path), Json) ?? new SpikeState()
                : new SpikeState();
        }
        catch (JsonException)
        {
            return new SpikeState();
        }
    }
}
