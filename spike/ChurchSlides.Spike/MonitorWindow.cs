using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ChurchSlides.Spike;

/// <summary>
/// A borderless topmost window placed at a monitor's exact pixel bounds with SetWindowPos (ARCH 7.5).
/// Base of the S1 test pattern and the S2 slide Output.
/// </summary>
public abstract class MonitorWindow : Window
{
    private readonly Stopwatch sinceShow = new();
    private readonly TaskCompletionSource rendered = new();

    protected MonitorWindow(MonitorInfo monitor)
    {
        Monitor = monitor;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Background = Brushes.Black;
        WindowStartupLocation = WindowStartupLocation.Manual;

        // A first guess in DIPs (at 100% they are pixels), so the window doesn't appear
        // elsewhere first. SetWindowPos then makes it exact.
        Left = monitor.Bounds.X;
        Top = monitor.Bounds.Y;
        Width = monitor.Bounds.Width;
        Height = monitor.Bounds.Height;

        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            PlaceExactly();
        };
        Loaded += (_, _) => PlaceExactly();
        ContentRendered += (_, _) =>
        {
            ShowToFirstFrameMs = sinceShow.Elapsed.TotalMilliseconds;
            rendered.TrySetResult();
        };
    }

    public MonitorInfo Monitor { get; }
    public double ShowToFirstFrameMs { get; private set; }
    public Task Rendered => rendered.Task;
    protected IntPtr Handle { get; private set; }

    public void ShowTimed()
    {
        sinceShow.Restart();
        Show();
    }

    /// <summary>Shows the window and waits up to 5 s for its first frame. False if it didn't render.</summary>
    public async Task<bool> ShowAndWait()
    {
        ShowTimed();
        return await Task.WhenAny(Rendered, Task.Delay(5000)) == Rendered;
    }

    private void PlaceExactly()
    {
        var b = Monitor.Bounds;
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, Native.SWP_NOACTIVATE);
    }
}
