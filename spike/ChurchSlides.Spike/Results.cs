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
    public const string S3 = "S3 Rich text";
    public const string S4 = "S4 Audio";
    public const string S4Cues = "S4 Cues";
    public const string S4Devices = "S4 Device log";
    public const string S5 = "S5 Timer";
    public const string S6 = "S6 Memory and decode";
    public const string S7 = "S7 Startup";
    public const string S8 = "S8 System";

    /// <summary>Section order in the results file. Later chunks add their sections here.</summary>
    public static readonly string[] SectionOrder = [S8, S1, S2, S3, S4, S4Cues, S4Devices, S5, S6, S7];

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
        new("S3-1", S3, "Rich text tab: paste a few verses from Word in each paste mode. The note under the toolbar says what was dropped. Note which mode the product should use."),
        new("S3-2", S3, "Tagged-run chips: Insert token, then the caret steps over it, Backspace and Delete remove it whole, Bold and a color apply to all of it, copy and paste inside the box keep it."),
        new("S3-3", S3, "Container chips: the same checks as S3-2."),
        new("S3-4", S3, "Typing in the 200-word sample (or any long box) feels immediate on this PC."),
        new("S3-5", S3, "Drag this window to the Output monitor. With Flick, editor and renderer look the same on the projector."),
        new("S3-6", S3, "Ctrl+Z and Ctrl+Y undo and redo typing and formatting, and the caret goes back to where it was."),
        new("S4-1", S4, "Audio tab: a song plays cleanly with the output the automatic run found best, with no crackle while slides change (play it, then step through slides on the Slides tab)."),
        new("S4-2", S4, "Audio tab, Sync: the white flash on the Output and the click are together."),
        new("S4-3", S4, "Audio tab, Tap: on play back, the Output changes where you tapped."),
        new("S4-4", S4,"Audio tab: play a song, then change the default device in Windows Sound settings (or plug in headphones). Say what happened, and whether Resume on the new default device worked (the log shows it)."),
        new("S4-5", S4, "Audio tab: clicking the position bar seeks and plays with no noticeable delay, with the output that the automatic run found best."),
        new("S5-1", S5, "Timer tab: while the timer runs, no flicker on the Output monitor or the projector."),
        new("S5-2", S5, "The digits don't move the text beside them, and the timer itself doesn't jiggle as digits change."),
        new("S5-3", S5, "The color changes to red at the warning threshold, and a minus sign shows once it goes past zero."),
        new("S5-4", S5, "The timer stays readable during the crossfades in the second half."),
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
