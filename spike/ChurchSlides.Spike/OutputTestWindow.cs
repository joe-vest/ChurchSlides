using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace ChurchSlides.Spike;

/// <summary>S1: the monitor window showing a test pattern. Esc closes it.</summary>
public sealed class OutputTestWindow : MonitorWindow
{
    public OutputTestWindow(MonitorInfo monitor)
        : base(monitor)
    {
        Title = "ChurchSlides spike: Output test";
        Content = new TestPattern(monitor);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
    }

    /// <summary>The window's actual rectangle and client size in pixels, compared with the monitor.</summary>
    public IReadOnlyList<Figure> Measure()
    {
        Native.GetWindowRect(Handle, out var window);
        Native.GetClientRect(Handle, out var client);
        var windowRect = window.ToPixelRect();
        var clientRect = client.ToPixelRect();
        var m = Monitor;
        var exact = windowRect == m.Bounds && clientRect.Width == m.Bounds.Width && clientRect.Height == m.Bounds.Height;
        var source = PresentationSource.FromVisual(this) as HwndSource;
        return
        [
            new("Output monitor", $"{m.DeviceName} {m.Bounds}, {m.DpiX} dpi, {m.RefreshHz} Hz"),
            new("Window rectangle (GetWindowRect)", windowRect.ToString()),
            new("Client size (GetClientRect)", $"{clientRect.Width}×{clientRect.Height}"),
            new("Matches monitor exactly", exact ? "yes" : "NO"),
            new("Show to first frame", $"{ShowToFirstFrameMs.ToString("F0", CultureInfo.InvariantCulture)} ms"),
            new("Window render mode", source?.CompositionTarget.RenderMode.ToString() ?? "?"),
        ];
    }
}

/// <summary>Edge lines one device pixel wide in four colors, corner marks, a centre cross and the bounds.</summary>
internal sealed class TestPattern(MonitorInfo monitor) : FrameworkElement
{
    private static readonly Brush Grey = Freeze(new SolidColorBrush(Color.FromRgb(90, 90, 90)));

    protected override void OnRender(DrawingContext dc)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var px = 1 / dpi.DpiScaleX;
        double w = ActualWidth, h = ActualHeight;

        dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h));

        // Edge lines: exactly the outermost row or column of pixels.
        dc.DrawRectangle(Brushes.Red, null, new Rect(0, 0, w, px));
        dc.DrawRectangle(Brushes.Lime, null, new Rect(w - px, 0, px, h));
        dc.DrawRectangle(Brushes.Blue, null, new Rect(0, h - px, w, px));
        dc.DrawRectangle(Brushes.Yellow, null, new Rect(0, 0, px, h));

        // Corner marks, inset 3 px so they don't hide the edge lines.
        var len = 60 * px;
        var thick = 4 * px;
        var inset = 3 * px;
        foreach (var (x, y, dx, dy) in new[] { (inset, inset, 1, 1), (w - inset, inset, -1, 1), (inset, h - inset, 1, -1), (w - inset, h - inset, -1, -1) })
        {
            dc.DrawRectangle(Brushes.White, null, Span(x, y, dx * len, dy * thick));
            dc.DrawRectangle(Brushes.White, null, Span(x, y, dx * thick, dy * len));
        }

        // Centre cross.
        dc.DrawRectangle(Grey, null, new Rect(w / 2 - 40 * px, h / 2, 80 * px, px));
        dc.DrawRectangle(Grey, null, new Rect(w / 2, h / 2 - 40 * px, px, 80 * px));

        Label(dc, "TOP (red)", w / 2, 12 * px, dpi);
        Label(dc, "BOTTOM (blue)", w / 2, h - 40 * px, dpi);
        Label(dc, "LEFT (yellow)", 90 * px, h / 2 - 12 * px, dpi);
        Label(dc, "RIGHT (green)", w - 90 * px, h / 2 - 12 * px, dpi);
        Label(dc, $"{monitor.DeviceName}  {monitor.Bounds}  {monitor.DpiX} dpi  {monitor.RefreshHz} Hz", w / 2, h / 2 + 60 * px, dpi);
        Label(dc, $"Window: {Math.Round(w * dpi.DpiScaleX)}×{Math.Round(h * dpi.DpiScaleY)} px.  Esc closes.", w / 2, h / 2 + 100 * px, dpi);
    }

    private static Rect Span(double x, double y, double dx, double dy) =>
        new(Math.Min(x, x + dx), Math.Min(y, y + dy), Math.Abs(dx), Math.Abs(dy));

    private static void Label(DrawingContext dc, string text, double centreX, double top, DpiScale dpi)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 22, Brushes.White, dpi.PixelsPerDip);
        dc.DrawText(ft, new Point(centreX - ft.Width / 2, top));
    }

    private static T Freeze<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
