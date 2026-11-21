using Avalonia;
using Avalonia.Controls;
using System;
using System.Linq;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>A row of weighted dashboard panels that becomes a single column on compact sheets.</summary>
/// <param name="gap">Space between panels in device-independent pixels.</param>
/// <param name="breakpoint">Content width at which panels stack.</param>
/// <param name="weights">Relative widths of the desktop tracks.</param>
internal sealed class DataColumns(double gap, double breakpoint, params double[] weights) : Panel
{
    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        var stacked = availableSize.Width <= breakpoint;
        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(new Size(WidthOf(availableSize.Width, i, stacked), double.PositiveInfinity));
        }

        var height = stacked ? Children.Sum(c => c.DesiredSize.Height) + (Math.Max(0, Children.Count - 1) * gap) : Children.Select(c => c.DesiredSize.Height).DefaultIfEmpty().Max();
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : Children.Sum(c => c.DesiredSize.Width), height);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var stacked = finalSize.Width <= breakpoint;
        double offset = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            var width = WidthOf(finalSize.Width, i, stacked);
            child.Arrange(new Rect(stacked ? 0 : offset, stacked ? offset : 0, width, stacked ? child.DesiredSize.Height : finalSize.Height));
            offset += (stacked ? child.DesiredSize.Height : width) + gap;
        }

        return finalSize;
    }

    /// <summary>Computes a CSS fractional track within the available width.</summary>
    /// <param name="width">Available content width.</param>
    /// <param name="i">Index of the panel.</param>
    /// <param name="stacked">Whether panels are arranged vertically.</param>
    /// <returns>Measured track width.</returns>
    private double WidthOf(double width, int i, bool stacked) => stacked || Children.Count == 0
        ? width
        : Math.Max(0, width - ((Children.Count - 1) * gap)) * (weights.Length == Children.Count ? weights[i] / weights.Sum() : 1d / Children.Count);
}
