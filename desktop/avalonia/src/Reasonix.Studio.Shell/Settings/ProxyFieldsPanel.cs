using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using System;
using System.Collections.Generic;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Wraps proxy fields in order and shares each line's spare width among its elastic fields.</summary>
public sealed class ProxyFieldsPanel : Panel
{
    /// <summary>Spacing within and between field lines.</summary>
    private const double Gap = 10;

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            if (child.IsVisible)
            {
                child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            }
        }

        return Layout(availableSize.Width, arrange: false);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        Layout(finalSize.Width, arrange: true);
        return finalSize;
    }

    /// <summary>Measures or arranges the wrapped lines for the same field widths.</summary>
    /// <param name="width">The line width.</param><param name="arrange">Whether to place the fields.</param>
    /// <returns>The occupied bounds.</returns>
    private Size Layout(double width, bool arrange)
    {
        var line = new List<(Control Field, double Basis, bool Grows)>();
        double y = 0, used = 0, maxWidth = 0;
        int growers = 0;
        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            var grows = child.HorizontalAlignment == HorizontalAlignment.Stretch && double.IsNaN(child.Width);
            var basis = Math.Min(width, grows ? child.MinWidth : child.DesiredSize.Width);
            if (line.Count > 0 && used + Gap + basis > width)
            {
                PlaceLine();
                y += Gap;
            }

            used += (line.Count == 0 ? 0 : Gap) + basis;
            line.Add((child, basis, grows));
            growers += grows ? 1 : 0;
        }

        PlaceLine();
        return new Size(maxWidth, y);

        void PlaceLine()
        {
            var extra = growers > 0 && double.IsFinite(width) ? Math.Max(0, width - used) / growers : 0;
            double height = 0, x = 0;
            foreach (var (field, basis, grows) in line)
            {
                if (!arrange)
                {
                    field.Measure(new Size(basis + (grows ? extra : 0), double.PositiveInfinity));
                }

                height = Math.Max(height, field.DesiredSize.Height);
            }

            foreach (var (field, basis, grows) in line)
            {
                var fieldWidth = basis + (grows ? extra : 0);
                if (arrange)
                {
                    field.Arrange(new Rect(x, y, fieldWidth, height));
                }

                x += fieldWidth + Gap;
            }

            maxWidth = Math.Max(maxWidth, used + (extra * growers));
            y += height;
            line.Clear();
            used = 0;
            growers = 0;
        }
    }
}
