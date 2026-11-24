using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace ChurchSlides.Spike;

/// <summary>A Timer slide for S5: a background (null: plain dark blue) and a line of static text above the Timer.</summary>
public sealed record TimerSlide(string? BackgroundPath, string Caption);

/// <summary>
/// The Timer's live slot (ARCH 7.4): each character drawn centred in a fixed-width cell, laid out
/// for the widest value (<c>-0:00:00</c>, hours with TMR-005's minus), so the digits
/// can't move whatever the font. A value is right-aligned in the cells. Glyphs are made once per
/// character and color.
/// </summary>
public sealed class TimerSlotLayout
{
    private const string Widest = "-0:00:00";

    private readonly Typeface face;
    private readonly double size;
    private readonly double[] cells;
    private readonly Dictionary<(char, Brush), FormattedText> glyphs = [];

    public TimerSlotLayout(FontFamily font, double size)
    {
        face = new Typeface(font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        this.size = size;
        var digit = "0123456789".Max(Advance);
        cells = [.. Widest.Select(c => char.IsDigit(c) ? digit : Advance(c))];
        // Make every glyph now, so no tick pays for the first use of a character or color.
        foreach (var c in "-0123456789:")
        {
            foreach (var brush in new[] { Normal, Warning, Shadow })
                Glyph(c, brush);
        }
    }

    public static readonly Brush Normal = Brushes.White;

    /// <summary>TMR-006: new Timers default to red.</summary>
    public static readonly Brush Warning = Brushes.Red;

    private static readonly Brush Shadow = Frozen(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)));

    public double Width => cells.Sum();
    public double Height => size * 1.3;

    /// <summary>
    /// Leading zero parts dropped (owner, 2026-11-24, changing TMR-009's h:mm:ss): <c>1:05:09</c>,
    /// then <c>12:00</c>, <c>9:59</c>, <c>:59</c>; a minus when negative (TMR-005). Right-aligned
    /// in the cells, every digit keeps its place as the leading parts drop away.
    /// </summary>
    public static string Format(int seconds)
    {
        var a = Math.Abs(seconds);
        int h = a / 3600, m = a / 60 % 60, s = a % 60;
        var body = h > 0 ? $"{h}:{m:00}:{s:00}" : m > 0 ? $"{m}:{s:00}" : $":{s:00}";
        return (seconds < 0 ? "-" : "") + body;
    }

    /// <summary>Draws <paramref name="value"/> with its top-left at <paramref name="origin"/>.</summary>
    public void Draw(DrawingContext dc, Point origin, string value, bool warning)
    {
        var brush = warning ? Warning : Normal;
        var offset = Math.Max(1, size / 60);
        var x = origin.X + Width;
        for (var i = value.Length - 1; i >= 0; i--)
        {
            var cell = cells[Math.Max(0, cells.Length - value.Length + i)];
            x -= cell;
            var c = value[i];
            var glyph = Glyph(c, brush);
            var at = new Point(x + (cell - glyph.WidthIncludingTrailingWhitespace) / 2, origin.Y);
            dc.DrawText(Glyph(c, Shadow), new Point(at.X + offset, at.Y + offset));
            dc.DrawText(glyph, at);
        }
    }

    /// <summary>How far proportional digits would move without cells: widest minus narrowest digit advance, in px.</summary>
    public static double DigitSpread(FontFamily font, double size)
    {
        var layout = new TimerSlotLayout(font, size);
        var advances = "0123456789".Select(layout.Advance).ToList();
        return advances.Max() - advances.Min();
    }

    private double Advance(char c) => Make(c, Normal).WidthIncludingTrailingWhitespace;

    private FormattedText Glyph(char c, Brush brush)
    {
        if (!glyphs.TryGetValue((c, brush), out var glyph))
            glyphs[(c, brush)] = glyph = Make(c, brush);
        return glyph;
    }

    private FormattedText Make(char c, Brush brush) =>
        new(c.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush, 1.0)
        {
            Trimming = TextTrimming.None,
        };

    private static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}

/// <summary>
/// The S5 Output (ARCH 7.4): two slide groups for crossfades, each a frozen background bitmap and
/// a frozen static-text bitmap (as C0.2b), plus one live slot, a <see cref="DrawingVisual"/> that
/// each tick redraws (only the visible groups'). Transitions ignore ticks (LIVE-032). Esc closes.
/// </summary>
public sealed class TimerOutput : MonitorWindow
{
    private readonly SlotGroup[] groups;
    private readonly (BitmapSource Background, BitmapSource Text)[] slides;
    private readonly TimerSlotLayout layout;
    private int front;
    private string value = "";
    private bool warning;
    private TaskCompletionSource? fade;

    public TimerOutput(MonitorInfo monitor, IReadOnlyList<TimerSlide> content, FontFamily font, double sizePx)
        : base(monitor)
    {
        Title = "ChurchSlides spike: Timer";
        UseLayoutRounding = true;
        int w = monitor.Bounds.Width, h = monitor.Bounds.Height;
        layout = new TimerSlotLayout(font, sizePx);
        var origin = new Point((w - layout.Width) / 2, h * 0.55 - layout.Height / 2);
        groups = [new(w, h, layout, origin), new(w, h, layout, origin)];
        slides = [.. content.Select(s => (BackgroundImage(s.BackgroundPath, w, h), Caption(s.Caption, w, h)))];

        var root = new Grid { Background = Brushes.Black };
        foreach (var g in groups)
            root.Children.Add(g);
        Content = root;
        Closed += (_, _) => FinishFade();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
    }

    public int Slide { get; private set; } = -1;

    /// <summary>Shows slide <paramref name="index"/>, Cut or crossfade; the incoming slot shows the current value first.</summary>
    public Task Show(int index, int fadeMs)
    {
        FinishFade();
        var outgoing = groups[front];
        var incoming = groups[1 - front];
        front = 1 - front;
        Slide = index;
        incoming.Set(slides[index]);
        incoming.Redraw(value, warning);
        Panel.SetZIndex(incoming, 1);
        Panel.SetZIndex(outgoing, 0);
        if (fadeMs <= 0)
        {
            incoming.Opacity = 1;
            outgoing.Release();
            return Task.CompletedTask;
        }
        var done = new TaskCompletionSource();
        fade = done;
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(fadeMs));
        animation.Completed += (_, _) =>
        {
            if (fade == done)
                FinishFade();
        };
        incoming.BeginAnimation(OpacityProperty, animation);
        return done.Task;
    }

    /// <summary>Redraws the visible slots with the new value. Returns the time taken in ms.</summary>
    public double SetValue(string text, bool isWarning)
    {
        var clock = Stopwatch.StartNew();
        value = text;
        warning = isWarning;
        foreach (var g in groups.Where(g => g.Visibility == Visibility.Visible))
            g.Redraw(text, isWarning);
        return clock.Elapsed.TotalMilliseconds;
    }

    private void FinishFade()
    {
        if (fade is not { } done)
            return;
        fade = null;
        var incoming = groups[front];
        incoming.BeginAnimation(OpacityProperty, null);
        incoming.Opacity = 1;
        groups[1 - front].Release();
        done.TrySetResult();
    }

    private static BitmapSource BackgroundImage(string? path, int w, int h)
    {
        if (path is not null)
            return new SlideFactory(w, h).Background(path, FitMode.Fill);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(16, 32, 72)), null, new Rect(0, 0, w, h));
        return Rasterize(visual, w, h);
    }

    private static BitmapSource Caption(string text, int w, int h)
    {
        var visual = new DrawingVisual();
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale);
        using (var dc = visual.RenderOpen())
        {
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), h / 14.0, Brushes.White, 1.0)
            {
                MaxTextWidth = w * 0.86,
                TextAlignment = TextAlignment.Center,
                Trimming = TextTrimming.None,
            };
            dc.DrawText(formatted, new Point(w * 0.07, h * 0.28 - formatted.Height / 2));
        }
        return Rasterize(visual, w, h);
    }

    private static BitmapSource Rasterize(Visual visual, int w, int h)
    {
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>One slide: background and static text images filling the window, and the live slot above them.</summary>
    private sealed class SlotGroup : Grid
    {
        private readonly Image background = new() { Stretch = Stretch.Fill };
        private readonly Image text = new() { Stretch = Stretch.Fill };
        private readonly SlotHost slot;

        public SlotGroup(int w, int h, TimerSlotLayout layout, Point origin)
        {
            slot = new SlotHost(layout, origin) { Width = w, Height = h };
            Visibility = Visibility.Collapsed;
            Children.Add(background);
            Children.Add(text);
            // The slot is laid out in Output pixels; the Viewbox maps them onto the window whatever the DPI.
            Children.Add(new Viewbox { Stretch = Stretch.Fill, Child = slot });
        }

        public void Set((BitmapSource Background, BitmapSource Text) slide)
        {
            background.Source = slide.Background;
            text.Source = slide.Text;
            Opacity = 0;
            Visibility = Visibility.Visible;
        }

        public void Redraw(string value, bool warning) => slot.Redraw(value, warning);

        public void Release()
        {
            Visibility = Visibility.Collapsed;
            background.Source = null;
            text.Source = null;
        }
    }

    /// <summary>Hosts the slot's DrawingVisual, the only thing a tick changes.</summary>
    private sealed class SlotHost : FrameworkElement
    {
        private readonly DrawingVisual visual = new();
        private readonly TimerSlotLayout layout;
        private readonly Point origin;

        public SlotHost(TimerSlotLayout layout, Point origin)
        {
            this.layout = layout;
            this.origin = origin;
            TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale);
            AddVisualChild(visual);
        }

        protected override int VisualChildrenCount => 1;

        public void Redraw(string value, bool warning)
        {
            using var dc = visual.RenderOpen();
            layout.Draw(dc, origin, value, warning);
        }

        protected override Visual GetVisualChild(int index) => visual;
    }
}
