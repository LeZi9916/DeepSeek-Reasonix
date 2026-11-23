using Avalonia;
using Avalonia.Controls;
using System;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>Hosts persistent Git commands and review above an independently scrolling patch list.</summary>
public sealed partial class GitChangesView : UserControl
{
    /// <summary>The formatted count displayed on the changes tab.</summary>
    public static readonly DirectProperty<GitChangesView, string?> TabCountProperty =
        AvaloniaProperty.RegisterDirect<GitChangesView, string?>(nameof(TabCount), view => view.TabCount);

    /// <summary>The current nonzero count of changed files.</summary>
    private string? _tabCount;

    /// <summary>The row whose heading is currently pinned to the scrolling viewport.</summary>
    private GitChangeRow? _pinned;

    /// <summary>Initializes a new instance of the <see cref="GitChangesView"/> class.</summary>
    public GitChangesView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => ReviewViewport.MaxHeight = Math.Max(120, e.NewSize.Height - 80);
        ToolbarLayout.SizeChanged += (_, e) => BranchName.MaxWidth = e.NewSize.Width * 0.28;
        Body.ScrollChanged += (_, _) => PinHeading();
        ChangeFiles.LayoutUpdated += (_, _) => PinHeading();
    }

    /// <summary>Gets the fixed command bar.</summary>
    internal ContentControl Commands => CommandHost;

    /// <summary>Gets the review shared across status refreshes.</summary>
    internal StackPanel Review => ReviewHost;

    /// <summary>Gets mutation and read feedback.</summary>
    internal TextBlock Notice => Feedback;

    /// <summary>Gets the scrolling changed-file list.</summary>
    internal StackPanel Files => ChangeFiles;

    /// <summary>Gets the count displayed beside the tab label.</summary>
    internal string? TabCount => _tabCount;

    /// <summary>Updates the tab count after an authoritative read.</summary>
    /// <param name="count">Formatted nonzero count, if any.</param>
    internal void UpdateTabCount(string? count) => SetAndRaise(TabCountProperty, ref _tabCount, count);

    /// <summary>Shows the status returned for the reviewed repository.</summary>
    /// <param name="branch">Current branch.</param>
    /// <param name="summary">Localized file totals.</param>
    /// <param name="added">Formatted insertion count.</param>
    /// <param name="removed">Formatted deletion count.</param>
    /// <param name="empty">The localized empty hint, when there are no files.</param>
    internal void Summarize(string? branch, string summary, string added, string removed, string? empty)
    {
        BranchName.Text = branch;
        BranchName.IsVisible = !string.IsNullOrEmpty(branch);
        ToolTip.SetTip(BranchName, branch);
        FileTotal.Text = summary;
        Added.Text = added;
        Removed.Text = removed;
        ToolTip.SetTip(ChangeTotal, $"{summary} {added} {removed}");
        NoChanges.Text = empty;
        NoChanges.IsVisible = empty is not null;
    }

    /// <summary>Pins the heading until its own section scrolls out of the viewport.</summary>
    private void PinHeading()
    {
        var offset = Body.Offset.Y;
        var lower = 0;
        var upper = ChangeFiles.Children.Count - 1;
        GitChangeRow? row = null;
        while (lower <= upper)
        {
            var index = lower + ((upper - lower) / 2);
            var child = ChangeFiles.Children[index];
            if (offset < child.Bounds.Y)
            {
                upper = index - 1;
            }
            else if (offset >= child.Bounds.Bottom)
            {
                lower = index + 1;
            }
            else
            {
                row = child as GitChangeRow;
                break;
            }
        }

        if (_pinned != row)
        {
            _pinned?.Pin(0);
            _pinned = row;
        }

        row?.Pin(offset - row.Bounds.Y);
    }
}
