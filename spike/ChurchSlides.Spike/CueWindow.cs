using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ChurchSlides.Spike;

/// <summary>The ear checks' Output (S4-2 Sync, S4-3 Tap): a full-screen color with a caption, changed by Cues. Esc closes.</summary>
public sealed class CueWindow : MonitorWindow
{
    private readonly Grid root = new() { Background = Brushes.Black };
    private readonly TextBlock caption = new()
    {
        Foreground = Brushes.White,
        FontSize = 96,
        FontWeight = FontWeights.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public CueWindow(MonitorInfo monitor)
        : base(monitor)
    {
        Title = "ChurchSlides spike: Cues";
        root.Children.Add(caption);
        Content = root;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
    }

    public void Set(Brush background, string text)
    {
        root.Background = background;
        caption.Text = text;
    }
}
