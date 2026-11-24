using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ChurchSlides.Spike;

public partial class MainWindow : Window
{
    private readonly Results results = new(AppContext.BaseDirectory);
    private readonly Metrics metrics = new();
    private readonly FrameCounter frames = new();
    private readonly DispatcherTimer readout;
    private readonly RenderThread renderThread = new();
    private readonly RichTextPanel richText = new();
    private readonly AudioPanel audio;
    private OutputTestWindow? pattern;
    private SlideSession? slides;
    private bool testRunning;
    private bool firstFrameSeen;

    public MainWindow()
    {
        InitializeComponent();
        audio = new AudioPanel(log => results.SetFigures(Results.S4Devices, log));

        readout = new DispatcherTimer(Metrics.Interval, DispatcherPriority.Background, (_, _) => ShowMetrics(), Dispatcher);
        readout.Start();

        OutputPanel.Children.Add(new ChecklistPanel(results, Results.S1));
        SlidesPanel.Children.Add(new ChecklistPanel(results, Results.S2));
        StartupPanel.Children.Add(new ChecklistPanel(results, Results.S7));
        RichTextTab.Content = richText;
        richText.ExtraPanel.Children.Add(new ChecklistPanel(results, Results.S3));
        richText.RunButton.Click += async (_, _) =>
        {
            await RunRichText();
            TrySave();
        };
        AudioTab.Content = audio;
        audio.ExtraPanel.Children.Add(new ChecklistPanel(results, Results.S4));
        audio.RunButton.Click += async (_, _) =>
        {
            await RunAudio();
            TrySave();
        };
        FitBox.ItemsSource = Enum.GetValues<FitMode>();
        FitBox.SelectedItem = FitMode.Fill;
        ShowMediaFolder();
        LoadMonitors();
        RefreshSystem();

        ContentRendered += OnContentRendered;
        PreviewKeyDown += (_, e) =>
        {
            if (Tabs.SelectedItem == SlidesTab && e.OriginalSource is not TextBox && SlideKey(e.Key))
                e.Handled = true;
        };
        Closing += (_, _) =>
        {
            pattern?.Close();
            slides?.Output.Close();
            renderThread.Dispose();
            audio.Dispose();
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
        else if (Startup.AutoS3Requested)
            _ = RunThenClose(RunRichText);
        else if (Startup.AutoS4Requested)
            _ = RunThenClose(RunAudio);
    }

    private async Task RunThenClose(Func<Task> run)
    {
        await run();
        Close();
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
        if (!await window.ShowAndWait())
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
            TrySave();

            // Memory first, so its "first read" decode times come before anything else reads the files.
            await RunSlideTest(Results.S6, MemoryResultText, t => t.RunS6());
            TrySave();
            await RunSlideTest(Results.S2, SlidesResultText, t => t.RunS2());
            TrySave();
            await RunRichText();
            TrySave();
            await RunAudio();

            var path = TrySave();
            AutoStatusText.Text = path is null ? "Done, but the results couldn't be saved." : $"Done. Saved {Path.GetFileName(path)}.";
            RefreshPreview();
        }
        finally
        {
            RunAutoButton.IsEnabled = true;
        }
    }

    // ---- Slides (S2) and Memory (S6) ----

    private async void RunSlides_Click(object sender, RoutedEventArgs e)
    {
        await RunSlideTest(Results.S2, SlidesResultText, t => t.RunS2());
        TrySave();
    }

    private async void RunMemory_Click(object sender, RoutedEventArgs e)
    {
        await RunSlideTest(Results.S6, MemoryResultText, t => t.RunS6());
        TrySave();
    }

    /// <summary>Runs an automatic slide test on the Output monitor and records its figures.</summary>
    private async Task RunSlideTest(string section, TextBlock show, Func<SlideTests, Task<List<Figure>>> run)
    {
        if (testRunning)
            return;
        var media = Media.Scan(results.State.MediaFolder);
        var problem = MonitorBox.SelectedItem is not MonitorInfo ? "no monitor found"
            : media.Backgrounds.Count == 0 ? "no backgrounds: choose the media folder (top right)"
            : null;
        if (problem is not null)
        {
            results.SetFigures(section, [new Figure("Not run", problem)]);
            show.Text = $"Not run: {problem}.";
            return;
        }

        testRunning = true;
        RunSlidesButton.IsEnabled = RunMemoryButton.IsEnabled = false;
        pattern?.Close();
        slides?.Output.Close();
        try
        {
            var tests = new SlideTests((MonitorInfo)MonitorBox.SelectedItem, media, renderThread, metrics, s => AutoStatusText.Text = s);
            var figures = await run(tests);
            results.SetFigures(section, figures);
            show.Text = string.Join(Environment.NewLine, figures.Select(f => $"{f.Name}: {f.Value}"));
            AutoStatusText.Text = $"{section}: done.";
        }
        finally
        {
            testRunning = false;
            RunSlidesButton.IsEnabled = RunMemoryButton.IsEnabled = true;
        }
    }

    /// <summary>The manual Output: the window, the presenter and a pre-renderer for each place.</summary>
    private sealed record SlideSession(SlideOutput Output, Presenter Presenter, PreRenderer OnUiThread, PreRenderer OnRenderThread);

    private async void OpenSlides_Click(object sender, RoutedEventArgs e)
    {
        if (testRunning || MonitorBox.SelectedItem is not MonitorInfo monitor)
            return;
        var media = Media.Scan(results.State.MediaFolder);
        var chosen = BackgroundList.SelectedItems.Cast<ListBoxItem>().Select(i => (string)i.Tag).ToList();
        if (chosen.Count == 0)
            chosen = [.. media.Backgrounds];
        if (chosen.Count == 0)
        {
            SlidesResultText.Text = "Choose the media folder (top right) first.";
            return;
        }

        slides?.Output.Close();
        pattern?.Close();
        int w = monitor.Bounds.Width, h = monitor.Bounds.Height;
        var output = new SlideOutput(monitor, media.LogoBitmap(w, h));
        var onUi = new PreRenderer(new SlideFactory(w, h), null);
        var onThread = new PreRenderer(new SlideFactory(w, h), renderThread);
        var presenter = new Presenter(output, TestContent.HymnSlides(chosen, (FitMode)FitBox.SelectedItem), onThread, new FrameMonitor(monitor.RefreshHz));
        var session = new SlideSession(output, presenter, onUi, onThread);
        output.KeyPressed += key => SlideKey(key);
        output.Closed += (_, _) =>
        {
            if (slides == session)
                slides = null;
        };
        slides = session;

        if (!await output.ShowAndWait())
        {
            SlidesResultText.Text = "The Output didn't render within 5 s.";
            output.Close();
            return;
        }
        await GoTo(0);
    }

    private void CloseSlides_Click(object sender, RoutedEventArgs e) => slides?.Output.Close();

    private async void Previous_Click(object sender, RoutedEventArgs e) => await Step(-1);

    private async void Next_Click(object sender, RoutedEventArgs e) => await Step(1);

    private void Black_Click(object sender, RoutedEventArgs e) => SlideKey(Key.B);

    private void Clear_Click(object sender, RoutedEventArgs e) => SlideKey(Key.C);

    private void Logo_Click(object sender, RoutedEventArgs e) => SlideKey(Key.L);

    /// <summary>The operator keys (as the real hotkeys). True if the key was one of them.</summary>
    private bool SlideKey(Key key)
    {
        if (slides?.Output is not { } output)
            return false;
        switch (key)
        {
            case Key.Right or Key.PageDown:
                _ = Step(1);
                return true;
            case Key.Left or Key.PageUp:
                _ = Step(-1);
                return true;
            case Key.B:
                output.SetBlack(!output.IsBlack);
                return true;
            case Key.C:
                output.SetClear(!output.IsClear);
                return true;
            case Key.L:
                output.SetLogo(!output.IsLogo);
                return true;
            default:
                return false;
        }
    }

    private Task Step(int by) => slides is null ? Task.CompletedTask : GoTo(slides.Presenter.Index + by);

    private async Task GoTo(int index)
    {
        if (slides is not { } session)
            return;
        var presenter = session.Presenter;
        presenter.Renderer = UiThreadRadio.IsChecked == true ? session.OnUiThread : session.OnRenderThread;
        var fadeMs = FadeRadio.IsChecked == true ? (int)FadeSlider.Value : 0;
        if (await presenter.GoTo(index, fadeMs) is { } result)
            SlidesResultText.Text = $"Slide {index + 1} of {presenter.Count}. {result} Pre-render on the {presenter.Renderer.Where}.";
    }

    private void LoadBackgrounds() =>
        BackgroundList.ItemsSource = Media.Scan(results.State.MediaFolder).Backgrounds
            .Select(p => new ListBoxItem { Content = Path.GetFileName(p), Tag = p })
            .ToList();

    // ---- Rich text (S3) ----

    /// <summary>Runs the S3 automatic run on the Rich text tab (shown while it runs) and records its figures.</summary>
    private async Task RunRichText()
    {
        if (testRunning)
            return;
        testRunning = true;
        richText.RunButton.IsEnabled = false;
        var previous = Tabs.SelectedItem;
        Tabs.SelectedItem = RichTextTab;
        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background); // let the tab lay out first
            var figures = await new RichTextTests(richText, s => AutoStatusText.Text = s).Run();
            results.SetFigures(Results.S3, figures);
            richText.ResultText.Text = string.Join(Environment.NewLine, figures.Select(f => $"{f.Name}: {f.Value}"));
            AutoStatusText.Text = $"{Results.S3}: done.";
        }
        finally
        {
            Tabs.SelectedItem = previous;
            testRunning = false;
            richText.RunButton.IsEnabled = true;
        }
    }

    // ---- Audio (S4) ----

    /// <summary>Runs the S4 automatic run on the Audio tab and records its figures.</summary>
    private async Task RunAudio()
    {
        if (testRunning)
            return;
        testRunning = true;
        audio.RunButton.IsEnabled = false;
        audio.StopManual();
        var previous = Tabs.SelectedItem;
        Tabs.SelectedItem = AudioTab;
        try
        {
            var files = AudioFiles.Scan(results.State.MediaFolder);
            var figures = await new AudioTests(files, metrics, s => AutoStatusText.Text = s).Run();
            results.SetFigures(Results.S4, figures);
            audio.ResultText.Text = string.Join(Environment.NewLine, figures.Select(f => $"{f.Name}: {f.Value}"));
            AutoStatusText.Text = $"{Results.S4}: done.";
        }
        finally
        {
            Tabs.SelectedItem = previous;
            testRunning = false;
            audio.RunButton.IsEnabled = true;
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

    private void ShowMediaFolder()
    {
        MediaFolderText.Text = results.State.MediaFolder is { } folder ? $"Media: {folder}" : "Media: not chosen";
        LoadBackgrounds();
        audio.SetFolder(results.State.MediaFolder);
        richText.SetBackground(Media.Scan(results.State.MediaFolder).Backgrounds.FirstOrDefault());
    }

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
