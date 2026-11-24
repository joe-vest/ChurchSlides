using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ChurchSlides.Spike;

/// <summary>The Timer tab (S5): font, size, duration, warning threshold and run length, and the run button.</summary>
public sealed class TimerPanel : DockPanel
{
    private readonly ComboBox fontBox = new() { Width = 200, Margin = new Thickness(6, 0, 0, 0) };
    private readonly TextBox sizeBox = Box("16", 50);
    private readonly TextBox durationBox = Box("11:00", 60);
    private readonly TextBox warningBox = Box("60", 50);
    private readonly TextBox runBox = Box("12", 50);

    public TimerPanel()
    {
        Margin = new Thickness(8);
        var fonts = Fonts.SystemFontFamilies.Select(f => f.Source).Order(StringComparer.OrdinalIgnoreCase).ToList();
        fontBox.ItemsSource = fonts;
        fontBox.SelectedItem = fonts.FirstOrDefault(f => f == "Segoe UI") ?? fonts.FirstOrDefault();

        RunButton = new Button { Content = "Run timer test", Padding = new Thickness(10, 4, 10, 4) };
        var settings = new WrapPanel
        {
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                Label("Font:"), fontBox,
                Label("Size (% of Output height):"), sizeBox,
                Label("Countdown from (m:ss):"), durationBox,
                Label("Warning at (s):"), warningBox,
                Label("Run length (min):"), runBox,
            },
        };
        ResultText = new TextBlock { FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        ExtraPanel = new StackPanel();
        Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children =
                        {
                            RunButton,
                            new TextBlock
                            {
                                Margin = new Thickness(12, 0, 0, 0),
                                VerticalAlignment = VerticalAlignment.Center,
                                TextWrapping = TextWrapping.Wrap,
                                Text = "Opens the Output on the monitor chosen on the Output tab and counts down, with the warning color at the threshold, then carries on negative. For the second half the Output crossfades between two Timer slides every 15 s. Esc on the Output stops it early. Part of the automatic run too (12 minutes).",
                            },
                        },
                    },
                    settings,
                    ResultText,
                    ExtraPanel,
                },
            },
        });
    }

    public Button RunButton { get; }
    public TextBlock ResultText { get; }

    /// <summary>For the checklist, below the results.</summary>
    public StackPanel ExtraPanel { get; }

    /// <summary>The choices, with the defaults where a box doesn't parse.</summary>
    public TimerSettings Settings()
    {
        var c = CultureInfo.InvariantCulture;
        var duration = TimeSpan.TryParseExact(durationBox.Text, @"m\:ss", c, out var d) ? (int)d.TotalSeconds : 660;
        return new TimerSettings(
            fontBox.SelectedItem as string ?? "Segoe UI",
            double.TryParse(sizeBox.Text, NumberStyles.Float, c, out var size) && size is > 1 and < 60 ? size : 16,
            duration,
            int.TryParse(warningBox.Text, NumberStyles.Integer, c, out var warning) ? warning : 60,
            double.TryParse(runBox.Text, NumberStyles.Float, c, out var run) && run > 0 ? run : 12);
    }

    private static TextBox Box(string text, double width) => new() { Text = text, Width = width, Margin = new Thickness(6, 0, 0, 0) };

    private static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
}
