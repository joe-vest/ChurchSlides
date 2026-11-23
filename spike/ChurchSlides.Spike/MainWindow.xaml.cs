using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ChurchSlides.Spike;

public partial class MainWindow : Window
{
    private readonly Results results = new(AppContext.BaseDirectory);
    private readonly Metrics metrics = new();
    private readonly FrameCounter frames = new();
    private readonly DispatcherTimer readout;
    private OutputTestWindow? pattern;
    private bool firstFrameSeen;

    public MainWindow()
    {
        InitializeComponent();

        readout = new DispatcherTimer(Metrics.Interval, DispatcherPriority.Background, (_, _) => ShowMetrics(), Dispatcher);
        readout.Start();

        OutputPanel.Children.Add(new ChecklistPanel(results, Results.S1));
        StartupPanel.Children.Add(new ChecklistPanel(results, Results.S7));
        ShowMediaFolder();
        LoadMonitors();
        RefreshSystem();

        ContentRendered += OnContentRendered;
        Closing += (_, _) =>
        {
            pattern?.Close();
            readout.Stop();
            metrics.Dispose();
            TrySave();
        };
    }

    // ---- Startup (S7) ----

    private void OnContentRendered(object? sender, EventArgs e)
    {
        if (firstFrameSeen)
            return;
        firstFrameSeen = true;
        var run = Startup.FirstFrame();
        results.AddStartupRun(run);
        TrySave();

        ThisRunText.Text = string.Create(CultureInfo.InvariantCulture,
            $"This launch: {run.Build} build, Kestrel {(run.Kestrel ? $"on at {Startup.KestrelUrl} ({run.KestrelMs:F0} ms)" : "off")}, " +
            $"{run.ToFirstFrameMs:F0} ms to first frame ({run.ToMainMs:F0} ms to Main), " +
            $"{run.JitMethods} methods JIT-compiled in {run.JitMs:F0} ms. Windows up {run.UptimeMinutes:F0} min.");
        StartupGrid.ItemsSource = results.State.StartupRuns.AsEnumerable().Reverse().ToList();

        if (Startup.AutoRequested)
            _ = RunAutoThenClose();
    }

    /// <summary>--auto: run the automatic tests, save, and exit (for remote runs and scripts).</summary>
    private async Task RunAutoThenClose()
    {
        await RunAuto();
        Close();
    }

    // ---- System (S8) ----

    private void RefreshSystem_Click(object sender, RoutedEventArgs e) => RefreshSystem();

    private void RefreshSystem()
    {
        var facts = SystemInfo.Collect().Select(f => new Figure(f.Name, f.Value)).ToList();
        SystemGrid.ItemsSource = facts;
        results.SetFigures(Results.S8, facts);
    }

    // ---- Output (S1) ----

    private void RefreshMonitors_Click(object sender, RoutedEventArgs e) => LoadMonitors();

    private void LoadMonitors()
    {
        var all = Monitors.All();
        MonitorBox.ItemsSource = all;
        MonitorBox.SelectedItem = Monitors.DefaultOutput(all);
    }

    private void OpenPattern_Click(object sender, RoutedEventArgs e)
    {
        if (MonitorBox.SelectedItem is MonitorInfo monitor)
            _ = MeasureOutput(monitor, closeAfter: false);
    }

    private void ClosePattern_Click(object sender, RoutedEventArgs e) => pattern?.Close();

    /// <summary>Opens the test pattern, waits for its first frame, checks its rectangle, records S1.</summary>
    private async Task MeasureOutput(MonitorInfo monitor, bool closeAfter)
    {
        pattern?.Close();
        var window = new OutputTestWindow(monitor);
        window.Closed += (_, _) =>
        {
            if (pattern == window)
                pattern = null;
        };
        pattern = window;
        window.ShowTimed();

        if (await Task.WhenAny(window.Rendered, Task.Delay(5000)) != window.Rendered)
        {
            OutputResultText.Text = "The test pattern didn't render within 5 s.";
            results.SetFigures(Results.S1, [new Figure("Output window", "did not render within 5 s")]);
            window.Close();
            return;
        }
        await Task.Delay(500); // let any late repositioning settle before measuring

        var figures = new List<Figure> { new("Monitors found", Monitors.All().Count.ToString(CultureInfo.InvariantCulture)) };
        figures.AddRange(window.Measure());
        results.SetFigures(Results.S1, figures);
        OutputResultText.Text = string.Join(Environment.NewLine, figures.Select(f => $"{f.Name}: {f.Value}"));
        if (closeAfter)
            window.Close();
    }

    // ---- Automatic run ----

    private async void RunAuto_Click(object sender, RoutedEventArgs e) => await RunAuto();

    private async Task RunAuto()
    {
        RunAutoButton.IsEnabled = false;
        try
        {
            AutoStatusText.Text = "System facts…";
            RefreshSystem();

            AutoStatusText.Text = "Output window…";
            LoadMonitors();
            if (MonitorBox.SelectedItem is MonitorInfo monitor)
                await MeasureOutput(monitor, closeAfter: true);

            var path = TrySave();
            AutoStatusText.Text = path is null ? "Done, but the results couldn't be saved." : $"Done. Saved {Path.GetFileName(path)}.";
            RefreshPreview();
        }
        finally
        {
            RunAutoButton.IsEnabled = true;
        }
    }

    // ---- Results ----

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var path = TrySave();
        if (path is not null)
            AutoStatusText.Text = $"Saved {Path.GetFileName(path)}.";
        RefreshPreview();
    }

    private string? TrySave()
    {
        try
        {
            return results.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AutoStatusText.Text = $"Can't write to {results.Folder}: {ex.Message} Copy the program to a folder you can write to.";
            return null;
        }
    }

    private void RefreshPreview_Click(object sender, RoutedEventArgs e) => RefreshPreview();

    private void RefreshPreview() => PreviewBox.Text = results.ToMarkdown();

    private void OpenFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{results.Folder}\"") { UseShellExecute = true });

    // ---- Media folder ----

    private void ChooseMedia_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Folder with the church's backgrounds", InitialDirectory = results.State.MediaFolder ?? "" };
        if (dialog.ShowDialog(this) != true)
            return;
        results.State.MediaFolder = dialog.FolderName;
        ShowMediaFolder();
        TrySave();
    }

    private void ShowMediaFolder() =>
        MediaFolderText.Text = results.State.MediaFolder is { } folder ? $"Media: {folder}" : "Media: not chosen";

    // ---- Live readouts ----

    private void CountFrames_Changed(object sender, RoutedEventArgs e) => frames.SetOn(CountFramesBox.IsChecked == true);

    private void ShowMetrics()
    {
        var s = metrics.Latest;
        var fps = frames.IsOn ? frames.FramesPerSecond.ToString("F0", CultureInfo.InvariantCulture) : "off";
        MetricsText.Text = string.Create(CultureInfo.InvariantCulture,
            $"Working set {s.WorkingSetMb,6:F1} MB   Private {s.PrivateMb,6:F1} MB   Managed {s.ManagedMb,5:F1} MB   CPU {s.CpuPercent,5:F1}%   Frames/s {fps}");
    }
}
