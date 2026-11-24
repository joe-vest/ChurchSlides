using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ChurchSlides.Spike;

/// <summary>
/// The Audio tab (S4): file, reader and output pickers, Play / Stop / Restart with Trim Start and
/// Trim End, the position at 0.1 s (AUD-011), a bar to click-to-seek-and-play (AUD-010), and the
/// device log with "Resume on the new default device" (C0.4 decision 6).
/// </summary>
public sealed class AudioPanel : DockPanel, IDisposable
{
    private const int MaxLogLines = 40;

    private readonly ComboBox fileBox = new() { Width = 260, Margin = new Thickness(6, 0, 0, 0) };
    private readonly ComboBox readerBox = new() { Width = 170, Margin = new Thickness(6, 0, 0, 0), ItemsSource = Enum.GetValues<ReaderKind>(), SelectedIndex = 0 };
    private readonly ComboBox outputBox = new() { Width = 180, Margin = new Thickness(6, 0, 0, 0), ItemsSource = Enum.GetValues<OutputKind>().Select(AudioCombo.Label).ToList(), SelectedIndex = 1 };
    private readonly TextBox trimStartBox = new() { Width = 60, Text = "0", Margin = new Thickness(6, 0, 0, 0) };
    private readonly TextBox trimEndBox = new() { Width = 60, Text = "", Margin = new Thickness(6, 0, 0, 0), ToolTip = "Seconds; empty plays to the end" };
    private readonly TextBlock positionText = new() { FontFamily = new FontFamily("Consolas"), FontSize = 20, Margin = new Thickness(0, 8, 0, 4) };
    private readonly Rectangle barFill = new() { Fill = Brushes.SteelBlue, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    private readonly Grid bar;
    private readonly CheckBox followBox = new()
    {
        Content = "WasapiPlayer follows the default device (Windows stream routing)",
        Margin = new Thickness(0, 8, 0, 0),
        ToolTip = "With it, Windows moves the stream to the new default device by itself.",
    };

    private readonly CheckBox resumeBox = new() { Content = "Resume on the new default device", Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock defaultText = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly ListBox logList = new() { Height = 150, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 4, 0, 0) };
    private readonly List<Figure> log = [];
    private readonly DeviceWatch devices = new();
    private readonly DispatcherTimer readout;
    private readonly Action<IReadOnlyList<Figure>> onLog;
    private SpikePlayer? player;
    private double lastPosition;
    private double lastErrorAt = double.NegativeInfinity;

    public AudioPanel(Action<IReadOnlyList<Figure>> onLog)
    {
        this.onLog = onLog;
        Margin = new Thickness(8);

        RunButton = new Button { Content = "Run audio test", Padding = new Thickness(10, 4, 10, 4) };
        var runRow = Row(RunButton, new TextBlock
        {
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Text = "About 5 minutes with a few songs: seeks in every MP3 with both readers, then each reader and output plays 20 s of the first VBR song, heard through loopback. Keep the volume low but not muted. Part of the automatic run too (--auto-s4 runs it alone).",
        });

        var play = Button("Play", async () => await PlayFrom(TrimStart));
        var stop = Button("Stop", async () => await (player?.StopAsync() ?? Task.CompletedTask));
        var restart = Button("Restart", async () => await PlayFrom(TrimStart));
        var pickers = Row(
            new TextBlock { Text = "File:", VerticalAlignment = VerticalAlignment.Center }, fileBox,
            new TextBlock { Text = "Reader:", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }, readerBox,
            new TextBlock { Text = "Output:", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }, outputBox);
        pickers.Margin = new Thickness(0, 12, 0, 0);
        var controls = Row(
            play, stop, restart,
            new TextBlock { Text = "Trim Start (s):", Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }, trimStartBox,
            new TextBlock { Text = "Trim End (s):", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }, trimEndBox);
        controls.Margin = new Thickness(0, 8, 0, 0);

        bar = new Grid { Height = 24, Background = Brushes.Gainsboro, Cursor = Cursors.Hand, Children = { barFill }, ToolTip = "Click to seek and play" };
        bar.MouseLeftButtonDown += async (_, e) =>
        {
            if (Player() is { } p)
                await PlayFrom(e.GetPosition(bar).X / bar.ActualWidth * p.Length.TotalSeconds);
        };

        foreach (var box in new[] { fileBox, readerBox, outputBox })
            box.SelectionChanged += (_, _) => DropPlayer();
        devices.Changed += OnDeviceEvent;
        followBox.Checked += (_, _) => DropPlayer();
        followBox.Unchecked += (_, _) => DropPlayer();
        Log($"Default output at start: {devices.DefaultOutputName()}");

        var stack = new StackPanel
        {
            Children =
            {
                runRow, pickers, controls, positionText, bar,
                new TextBlock
                {
                    Margin = new Thickness(0, 12, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    Text = "Device change (S4-4): play a song, then choose another default device in Windows Sound settings, or plug in headphones. The projector's VGA splitter carries no sound, so its audio isn't the case to test. Every notification and the output's stop event are logged here and saved with the results.",
                },
                followBox, resumeBox, defaultText, logList,
            },
        };
        ResultText = new TextBlock { FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        ExtraPanel = new StackPanel();
        stack.Children.Add(ResultText);
        stack.Children.Add(ExtraPanel);
        Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack });

        readout = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => ShowPosition(), Dispatcher);
        readout.Start();
    }

    public Button RunButton { get; }
    public TextBlock ResultText { get; }

    /// <summary>For the checklist, below the results.</summary>
    public StackPanel ExtraPanel { get; }

    private double TrimStart => double.TryParse(trimStartBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 0;

    private double? TrimEnd => double.TryParse(trimEndBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : null;

    public void SetFolder(string? folder)
    {
        var files = AudioFiles.Scan(folder);
        fileBox.ItemsSource = files.Select(f => new ComboBoxItem { Content = System.IO.Path.GetFileName(f), Tag = f }).ToList();
        fileBox.SelectedIndex = files.Count > 0 ? 0 : -1;
    }

    /// <summary>Stops manual playback (the automatic run needs the device to itself).</summary>
    public void StopManual() => DropPlayer();

    public void Dispose()
    {
        readout.Stop();
        DropPlayer();
        devices.Dispose();
    }

    private async Task PlayFrom(double seconds)
    {
        if (Player() is not { } p)
            return;
        try
        {
            await p.PlayFrom(seconds, TrimEnd);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NAudio.MmException)
        {
            Log($"Play failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>The player for the picked file, reader and output, made on first use.</summary>
    private SpikePlayer? Player()
    {
        if (player is not null)
            return player;
        if (fileBox.SelectedItem is not ComboBoxItem { Tag: string path })
        {
            positionText.Text = "No MP3s: choose the media folder (top right).";
            return null;
        }
        try
        {
            player = new SpikePlayer(path, new AudioCombo((ReaderKind)readerBox.SelectedItem, (OutputKind)outputBox.SelectedIndex), followBox.IsChecked == true);
            player.Stopped += OnStopped;
            return player;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NAudio.MmException or IOException)
        {
            Log($"Open failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private void DropPlayer()
    {
        player?.Dispose();
        player = null;
    }

    private void OnStopped(Exception? error)
    {
        if (error is null)
        {
            Log("Output stopped (end or Trim End)");
            return;
        }
        lastErrorAt = AudioClock.Seconds;
        Log($"Output stopped with {error.GetType().Name}: {error.Message}");
    }

    private async void OnDeviceEvent(DeviceEvent e)
    {
        Log(e.Text);
        if (!e.DefaultOutputChanged || resumeBox.IsChecked != true || player is not { } p)
            return;
        if (!p.IsPlaying && AudioClock.Seconds - lastErrorAt > 3)
            return;
        try
        {
            var at = p.IsPlaying ? p.SafeSeconds() : lastPosition;
            var ms = await p.ResumeOnNewDevice(at);
            Log(string.Create(CultureInfo.InvariantCulture, $"Resumed at {SpikePlayer.Clock(at)} on {devices.DefaultOutputName()} in {ms:F0} ms"));
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NAudio.MmException)
        {
            Log($"Resume failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void ShowPosition()
    {
        defaultText.Text = $"Default output: {devices.DefaultOutputName()}";
        if (player is not { } p)
            return;
        var length = p.Length.TotalSeconds;
        if (p.IsPlaying)
            lastPosition = p.SafeSeconds();
        positionText.Text = $"{SpikePlayer.Clock(lastPosition)} / {SpikePlayer.Clock(length)}   {p.Combo}{(p.FollowDefault ? ", following the default device" : "")}";
        barFill.Width = length > 0 ? Math.Clamp(lastPosition / length, 0, 1) * bar.ActualWidth : 0;
    }

    private void Log(string text)
    {
        var line = new Figure(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture), text);
        log.Add(line);
        if (log.Count > MaxLogLines)
            log.RemoveAt(0);
        logList.Items.Add($"{line.Name}  {line.Value}");
        if (logList.Items.Count > MaxLogLines)
            logList.Items.RemoveAt(0);
        logList.ScrollIntoView(logList.Items[^1]);
        onLog(log);
    }

    private static Button Button(string text, Func<Task> click)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
        button.Click += async (_, _) => await click();
        return button;
    }

    private static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in children)
            row.Children.Add(c);
        return row;
    }
}
