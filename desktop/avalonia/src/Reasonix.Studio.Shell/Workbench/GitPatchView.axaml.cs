using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>Displays unified hunks with independent old and new line numbers and selectable patch text.</summary>
public sealed partial class GitPatchView : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="GitPatchView"/> class.</summary>
    public GitPatchView() => InitializeComponent();

    /// <summary>Draws the supplied hunk ranges. A binary or header-only response remains readable as text.</summary>
    /// <param name="patch">Unmodified kernel patch.</param>
    /// <param name="truncated">Optional truncation notice.</param>
    /// <param name="open">Opens the working file when its line number is selected.</param>
    internal void Show(string patch, string? truncated, Func<Task>? open)
    {
        Lines.Children.Clear();
        var old = 0;
        var next = 0;
        var started = false;
        foreach (var line in patch.TrimEnd('\n').Split('\n'))
        {
            var header = Hunk().Match(line);
            if (header.Success)
            {
                started = true;
                old = int.Parse(header.Groups[1].Value, CultureInfo.InvariantCulture);
                next = int.Parse(header.Groups[2].Value, CultureInfo.InvariantCulture);
                var hunk = new SelectableTextBlock { Text = line, Margin = new Thickness(8, 2), FontSize = 11 };
                var band = new Border { Child = hunk };
                band.Bind(Border.BackgroundProperty, band.GetResourceObservable("OverlayBrush"));
                Lines.Children.Add(band);
                continue;
            }

            if (!started || line.Length == 0 || line[0] == '\\')
            {
                continue;
            }

            var added = line[0] == '+';
            var removed = line[0] == '-';
            var left = added ? string.Empty : (old++).ToString(CultureInfo.InvariantCulture);
            var right = removed ? string.Empty : (next++).ToString(CultureInfo.InvariantCulture);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("28,28,15,*"), MinHeight = 19 };
            var numbers = new Button { Content = left.PadLeft(3) + " " + right.PadLeft(3), Padding = default, FontSize = 11, MinWidth = 0, MinHeight = 0, IsEnabled = open is not null };
            numbers.Classes.Add("git-flat");
            Grid.SetColumnSpan(numbers, 2);
            if (open is not null)
            {
                numbers.Click += async (_, _) => await open().ConfigureAwait(true);
            }

            row.Children.Add(numbers);
            var sign = new TextBlock { Text = line[0].ToString(), HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(sign, 2);
            row.Children.Add(sign);
            var code = new SelectableTextBlock { Text = line[1..].TrimEnd('\r'), TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(code, 3);
            row.Children.Add(code);
            var behind = new Border { Child = row };
            if (added || removed)
            {
                var tint = new Border { Opacity = 0.12, IsHitTestVisible = false };
                tint.Bind(Border.BackgroundProperty, tint.GetResourceObservable(added ? "AddBrush" : "DelBrush"));
                var layers = new Grid();
                behind.Child = null;
                layers.Children.Add(tint);
                layers.Children.Add(row);
                behind.Child = layers;
            }

            Lines.Children.Add(behind);
        }

        if (!started)
        {
            Lines.Children.Add(new SelectableTextBlock { Text = patch, TextWrapping = TextWrapping.Wrap });
        }

        Truncated.Text = truncated;
        Truncated.IsVisible = truncated is not null;
    }

    /// <summary>Matches the old and new start positions of a unified diff hunk.</summary>
    /// <returns>The hunk header expression.</returns>
    [GeneratedRegex(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@")]
    private static partial Regex Hunk();
}
