using Avalonia.Controls;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>A tree row with disclosure, file icon, basename and authoritative Git status.</summary>
public sealed partial class WorkspaceFileRow : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="WorkspaceFileRow"/> class.</summary>
    public WorkspaceFileRow() => InitializeComponent();
}
