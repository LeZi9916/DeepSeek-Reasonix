using Avalonia;
using Avalonia.Controls;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>A fold preference that wraps its choices when there is no room beside the label.</summary>
public sealed partial class FoldingOptionRow : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="FoldingOptionRow"/> class.</summary>
    public FoldingOptionRow() => InitializeComponent();

    /// <summary>Sets the row copy and its actual interactive control.</summary>
    /// <param name="label">The setting name.</param><param name="hint">Its explanation.</param><param name="control">The choices or switch.</param>
    internal void Show(string label, string hint, Control control)
    {
        Caption.Text = label;
        Hint.Text = hint;
        Options.Content = control;
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        Options.Measure(new Size(System.Math.Max(0, availableSize.Width - 24), double.PositiveInfinity));
        var wrap = Options.DesiredSize.Width + 196 > availableSize.Width;
        Grid.SetColumn(Options, wrap ? 0 : 1);
        Grid.SetRow(Options, wrap ? 1 : 0);
        Options.Margin = wrap ? new Thickness(0, 8, 0, 0) : default;
        return base.MeasureOverride(availableSize);
    }
}
