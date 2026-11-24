using Avalonia;
using Avalonia.Controls;
using System;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Lays palette cards out in equal-width columns that fill the available width.</summary>
public sealed class PaletteGrid : Panel
{
    /// <summary>Gets or sets the narrowest column before another one is dropped.</summary>
    public double MinimumItemWidth { get; set; } = 196;

    /// <summary>Gets or sets the horizontal space between columns.</summary>
    public double ColumnGap { get; set; } = 12;

    /// <summary>Gets or sets the vertical space between rows.</summary>
    public double RowGap { get; set; } = 14;

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        var (columns, width) = Columns(availableSize.Width);
        var height = 0d;
        for (var row = 0; row * columns < Children.Count; row++)
        {
            if (row > 0)
            {
                height += RowGap;
            }

            height += MeasureRow(row, columns, width);
        }

        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : (width * columns) + ((columns - 1) * ColumnGap), height);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, width) = Columns(finalSize.Width);
        var y = 0d;
        for (var row = 0; row * columns < Children.Count; row++)
        {
            var count = Math.Min(columns, Children.Count - (row * columns));
            var height = 0d;
            for (var column = 0; column < count; column++)
            {
                height = Math.Max(height, Children[(row * columns) + column].DesiredSize.Height);
            }

            for (var column = 0; column < count; column++)
            {
                Children[(row * columns) + column].Arrange(new Rect(column * (width + ColumnGap), y, width, height));
            }

            y += height + RowGap;
        }

        return finalSize;
    }

    /// <summary>Measures one row and returns its tallest card.</summary>
    /// <param name="row">Zero-based row to measure.</param>
    /// <param name="columns">Columns available in this row.</param>
    /// <param name="width">Width assigned to each card.</param>
    /// <returns>The row height.</returns>
    private double MeasureRow(int row, int columns, double width)
    {
        var count = Math.Min(columns, Children.Count - (row * columns));
        var height = 0d;
        for (var column = 0; column < count; column++)
        {
            var child = Children[(row * columns) + column];
            child.Measure(new Size(width, double.PositiveInfinity));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return height;
    }

    /// <summary>Fits as many equal columns as the minimum width allows.</summary>
    /// <param name="available">Width offered by the parent.</param>
    /// <returns>The column count and the width each column receives.</returns>
    private (int Columns, double Width) Columns(double available)
    {
        var minimum = Math.Max(1, MinimumItemWidth);
        var finite = double.IsFinite(available) && available > 0 ? available : minimum;
        var columns = Math.Max(1, (int)Math.Floor((finite + ColumnGap) / (minimum + ColumnGap)));
        var width = Math.Max(1, (finite - ((columns - 1) * ColumnGap)) / columns);
        return (columns, width);
    }
}
