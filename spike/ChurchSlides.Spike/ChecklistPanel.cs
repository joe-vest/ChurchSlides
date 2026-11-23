using System.Windows;
using System.Windows.Controls;

namespace ChurchSlides.Spike;

/// <summary>The eye checks for one section: Pass / Fail and a note per item, written straight into the results.</summary>
public sealed class ChecklistPanel : StackPanel
{
    public ChecklistPanel(Results results, string section)
    {
        Margin = new Thickness(0, 12, 0, 0);
        Children.Add(new TextBlock { Text = "Eye checks (on the church PC, in person)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        foreach (var item in Results.Checklist.Where(i => i.Section == section))
        {
            var answer = results.State.Checks[item.Id];
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };

            var pass = Verdict("Pass", item.Id, answer);
            var fail = Verdict("Fail", item.Id, answer);
            var note = new TextBox { Text = answer.Note, Width = 260, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Note" };
            note.TextChanged += (_, _) => answer.Note = note.Text;

            foreach (var c in new UIElement[] { note, fail, pass })
            {
                DockPanel.SetDock(c, Dock.Right);
                row.Children.Add(c);
            }
            row.Children.Add(new TextBlock { Text = $"{item.Id}  {item.Text}", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            Children.Add(row);
        }
    }

    private static RadioButton Verdict(string verdict, string group, CheckAnswer answer)
    {
        var button = new RadioButton
        {
            Content = verdict,
            GroupName = group,
            IsChecked = answer.Verdict == verdict,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        button.Checked += (_, _) => answer.Verdict = verdict;
        return button;
    }
}
