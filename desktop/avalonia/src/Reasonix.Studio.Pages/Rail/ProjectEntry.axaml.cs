using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Reasonix.Studio.Contracts.Tree;
using System;

namespace Reasonix.Studio.Pages.Rail;

/// <summary>A project disclosure with its canonical path as the accessible identity.</summary>
internal sealed partial class ProjectEntry : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="ProjectEntry"/> class.</summary>
    /// <param name="workspace">Canonical project.</param>
    /// <param name="folded">Whether its sessions are hidden.</param>
    /// <param name="toggle">Changes the disclosure.</param>
    internal ProjectEntry(TreeWorkspace workspace, bool folded, Action toggle)
    {
        InitializeComponent();
        Title.Text = workspace.Name;
        Opacity = workspace.Missing ? 0.5 : 1;
        Chevron.Data = Geometry.Parse(folded ? "m9 6 6 6-6 6" : "m6 9 6 6 6-6");
        AutomationProperties.SetName(Fold, workspace.Name);
        ToolTip.SetTip(Fold, workspace.Root);
        Fold.Click += (_, _) => toggle();
    }
}
