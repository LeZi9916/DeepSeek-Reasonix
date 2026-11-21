using Avalonia.Controls;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>A staging checkbox whose state comes from the authoritative Git index.</summary>
public sealed class GitStageBox : CheckBox
{
    /// <summary>Keeps the displayed state unchanged until the host returns the updated index.</summary>
    protected override void Toggle()
    {
    }
}
