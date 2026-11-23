using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace ChurchSlides.Spike;

/// <summary>
/// The S2 Output (ARCH 7.1): two slide groups (background plus text) for Cut or Crossfade,
/// then the Logo and Black overlays on top. Clear hides the text of both groups.
/// Keys pressed here are passed to <see cref="KeyPressed"/>; Esc closes.
/// </summary>
public sealed class SlideOutput : MonitorWindow
{
    private readonly SlideGroup[] groups = [new(), new()];
    private readonly Border logo = new() { Background = Brushes.Black, Visibility = Visibility.Collapsed };
    private readonly Border black = new() { Background = Brushes.Black, Visibility = Visibility.Collapsed };
    private int front;
    private bool clear;
    private TaskCompletionSource? fade;

    public SlideOutput(MonitorInfo monitor, BitmapSource? logoBitmap)
        : base(monitor)
    {
        Title = "ChurchSlides spike: Output";
        UseLayoutRounding = true;
        logo.Child = new Image { Source = logoBitmap, Stretch = Stretch.Fill };

        var root = new Grid { Background = Brushes.Black };
        foreach (var g in groups)
            root.Children.Add(g);
        root.Children.Add(logo);
        root.Children.Add(black);
        Panel.SetZIndex(logo, 2); // above the slide groups, which swap between 0 and 1
        Panel.SetZIndex(black, 3);
        Content = root;
        Closed += (_, _) => FinishFade(); // so nobody waits for a fade that will never finish

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
            else
                KeyPressed?.Invoke(e.Key);
        };
    }

    public event Action<Key>? KeyPressed;

    public bool IsBlack => black.Visibility == Visibility.Visible;
    public bool IsLogo => logo.Visibility == Visibility.Visible;
    public bool IsClear => clear;

    /// <summary>
    /// Shows a slide. Cut when <paramref name="fadeMs"/> is 0, else the incoming group fades in
    /// over the outgoing one. Completes when the transition has finished. A transition still
    /// running is finished at once first.
    /// </summary>
    public Task Show(RenderedSlide slide, int fadeMs)
    {
        FinishFade();
        var outgoing = groups[front];
        var incoming = groups[1 - front];
        front = 1 - front;

        incoming.Set(slide, clear);
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

    public void SetBlack(bool on)
    {
        black.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on)
            logo.Visibility = Visibility.Collapsed;
    }

    /// <summary>Logo with no logo image is black (LIVE-012).</summary>
    public void SetLogo(bool on)
    {
        logo.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on)
            black.Visibility = Visibility.Collapsed;
    }

    public void SetClear(bool on)
    {
        clear = on;
        foreach (var g in groups)
            g.SetClear(on);
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

    /// <summary>One slide: its background and text bitmaps, each filling the window.</summary>
    private sealed class SlideGroup : Grid
    {
        private readonly Image background = new() { Stretch = Stretch.Fill };
        private readonly Image text = new() { Stretch = Stretch.Fill };

        public SlideGroup()
        {
            Visibility = Visibility.Collapsed;
            Children.Add(background);
            Children.Add(text);
        }

        public void Set(RenderedSlide slide, bool clear)
        {
            background.Source = slide.Background;
            text.Source = slide.Text;
            SetClear(clear);
            Opacity = 0;
            Visibility = Visibility.Visible;
        }

        public void SetClear(bool clear) => text.Visibility = clear ? Visibility.Hidden : Visibility.Visible;

        /// <summary>Hides the group and drops its bitmaps, so only the pre-render window holds slides.</summary>
        public void Release()
        {
            Visibility = Visibility.Collapsed;
            background.Source = null;
            text.Source = null;
        }
    }
}
