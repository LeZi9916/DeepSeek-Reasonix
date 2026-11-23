using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using System;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>Displays the unified diff used by file, change, and history views.</summary>
public sealed partial class GitPatchView : UserControl
{
    /// <summary>Opens the working file where this diff offers that action.</summary>
    private Func<Task>? _open;

    /// <summary>Initializes a new instance of the <see cref="GitPatchView"/> class.</summary>
    public GitPatchView() => InitializeComponent();

    /// <summary>Gets a value indicating whether the host patch contains displayable hunks.</summary>
    internal bool HasHunks => Rows.ItemCount > 0;

    /// <summary>Draws the supplied hunks with selectable code spanning all rows.</summary>
    /// <param name="patch">Unmodified kernel patch.</param><param name="label">Accessible file name.</param>
    /// <param name="empty">The localized empty state.</param><param name="truncated">The notice when the host shortened the patch.</param>
    /// <param name="open">Opens the working file on a line number click.</param>
    /// <param name="showMetadata">Whether commit history displays a non-empty header-only patch.</param>
    internal void Show(string patch, string? label, string empty, string? truncated, Func<Task>? open, bool showMetadata = false)
    {
        _open = open;
        Classes.Set("opens-line", open is not null);
        AutomationProperties.SetName(this, label ?? string.Empty);
        var rows = GitPatchLine.Read(patch);
        Rows.ItemsSource = rows;
        var metadataOnly = rows.Count == 0 && showMetadata && !string.IsNullOrWhiteSpace(patch);
        NumberedPatch.IsVisible = rows.Count > 0 || metadataOnly;
        Empty.IsVisible = !NumberedPatch.IsVisible;
        Empty.Text = empty;
        Truncated.Text = truncated;
        Truncated.IsVisible = truncated is not null;

        var code = Code.Inlines!;
        code.Clear();
        Code.Margin = metadataOnly ? default : new Thickness(74, 0, 0, 0);
        Code.TextWrapping = metadataOnly ? TextWrapping.Wrap : TextWrapping.NoWrap;
        if (metadataOnly)
        {
            code.Add(new Run(patch));
            return;
        }

        foreach (var row in rows)
        {
            if (code.Count > 0)
            {
                code.Add(new LineBreak());
            }

            var line = new Run(row.Text);
            if (row.IsHeading)
            {
                line.Bind(TextElement.ForegroundProperty, Code.GetResourceObservable("FaintBrush"));
            }

            code.Add(line);
        }
    }

    /// <summary>Opens the file when the reader activates either line number.</summary>
    /// <param name="sender">The clicked line number.</param><param name="e">The click.</param>
    private async void OpenLine(object? sender, RoutedEventArgs e)
    {
        if (_open is { } open)
        {
            await open().ConfigureAwait(true);
        }
    }
}
