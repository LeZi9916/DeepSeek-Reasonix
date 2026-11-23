using Avalonia.Controls;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>A performance preference with its own label and units.</summary>
public sealed partial class OpsLimitView : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="OpsLimitView"/> class.</summary>
    public OpsLimitView() => InitializeComponent();

    /// <summary>Gets the numeric field owned by this preference.</summary>
    internal SettingsUnitField Field => Editor;

    /// <summary>Applies the reference heading and input description.</summary>
    /// <param name="title">The indexed heading.</param>
    /// <param name="hint">The section explanation.</param>
    /// <param name="label">The input label.</param>
    /// <param name="detail">The accepted values.</param>
    internal void Configure(string title, string hint, string label, string detail)
    {
        Title.Text = title;
        ToolTip.SetTip(Title, hint);
        Label.Text = label;
        Detail.Text = detail;
        Editor.SetDigitWidth(50);
    }
}
