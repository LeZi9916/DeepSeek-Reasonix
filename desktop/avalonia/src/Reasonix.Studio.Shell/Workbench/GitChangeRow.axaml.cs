using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>Displays a changed-file heading and its optional patch.</summary>
public sealed partial class GitChangeRow : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="GitChangeRow"/> class.</summary>
    public GitChangeRow()
    {
        InitializeComponent();
    }

    /// <summary>Gets the patch body for the repository reader to populate.</summary>
    internal ContentControl Patch => PatchHost;

    /// <summary>Gets or sets a value indicating whether the patch is expanded.</summary>
    internal bool Expanded
    {
        get
        {
            return PatchHost.IsVisible;
        }

        set
        {
            PatchHost.IsVisible = value;
            FileHeader.Classes.Set("folded", !value);
            FoldButton.Content = value ? "⌄" : "›";
            PeekBody.IsVisible = PeekButton.IsVisible = !value && PeekLabel.Text is not null;
        }
    }

    /// <summary>Sets the fixed file metadata from a repository read.</summary>
    /// <param name="path">Workspace-relative path.</param>
    /// <param name="tooltip">Path or rename description.</param>
    /// <param name="status">Git status code.</param>
    /// <param name="stage">The Git staging checkbox.</param>
    /// <param name="insertions">Formatted insertions, if known.</param>
    /// <param name="deletions">Formatted deletions, if known.</param>
    /// <param name="tag">Badge on a large change list.</param>
    /// <param name="peek">Expansion hint on a short change list.</param>
    internal void ShowFile(string path, string tooltip, string status, GitStageBox stage, string? insertions, string? deletions, string? tag, string? peek)
    {
        FilePath.Text = path;
        ToolTip.SetTip(FilePath, tooltip);
        AutomationProperties.SetName(FoldButton, path);
        Status.Text = status;
        Status.Classes.Set("added", status is "A" or "??");
        Status.Classes.Set("deleted", status == "D");
        stage.Width = stage.Height = 14;
        stage.BorderThickness = new Thickness(1.5);
        StageHost.Content = stage;
        Insertions.Text = insertions;
        Insertions.IsVisible = insertions is not null;
        Deletions.Text = deletions;
        Deletions.IsVisible = deletions is not null;
        TagBody.IsVisible = tag is not null;
        FileTag.Text = tag;
        PeekLabel.Text = peek;
        if (peek is not null)
        {
            AutomationProperties.SetName(PeekButton, peek);
            AutomationProperties.SetAutomationId(PeekButton, "action:workbench.change-peek");
        }
    }

    /// <summary>Connects both expand controls to the repository's patch load.</summary>
    /// <param name="toggle">Updates the stored fold and loads an opened patch.</param>
    internal void OnToggle(Func<bool, Task> toggle)
    {
        FoldButton.Click += async (_, _) => await toggle(!Expanded).ConfigureAwait(true);
        PeekButton.Click += async (_, _) => await toggle(true).ConfigureAwait(true);
    }

    /// <summary>Connects the source-file button to the workbench.</summary>
    /// <param name="label">Visible, localized caption.</param>
    /// <param name="open">Opens the source file.</param>
    internal void OnOpen(string label, Func<Task> open)
    {
        OpenFileButton.Content = label;
        OpenFileButton.Click += async (_, _) => await open().ConfigureAwait(true);
    }

    /// <summary>Requires a second activation before discarding a tracked change.</summary>
    /// <param name="label">Initial button caption.</param>
    /// <param name="confirm">Caption when confirmation is required.</param>
    /// <param name="description">Localized accessible label containing the path.</param>
    /// <param name="discard">The confirmed repository mutation, if allowed.</param>
    internal void OnDiscard(string label, string confirm, string description, Func<Task>? discard)
    {
        DiscardButton.IsVisible = discard is not null;
        if (discard is null)
        {
            return;
        }

        DiscardButton.Content = label;
        AutomationProperties.SetName(DiscardButton, description);
        DiscardButton.LostFocus += (_, _) =>
        {
            DiscardButton.Content = label;
            DiscardButton.Classes.Remove("confirming");
        };
        DiscardButton.Click += async (_, _) =>
        {
            if (DiscardButton.Classes.Contains("confirming"))
            {
                await discard().ConfigureAwait(true);
                return;
            }

            DiscardButton.Content = confirm;
            DiscardButton.Classes.Add("confirming");
        };
    }

    /// <summary>Moves the heading down inside its own section without affecting layout.</summary>
    /// <param name="offset">Distance scrolled through the section.</param>
    internal void Pin(double offset) => ((TranslateTransform)FileHeader.RenderTransform!).Y =
        Math.Clamp(offset, 0, Math.Max(0, Bounds.Height - FileHeader.Bounds.Height));
}
