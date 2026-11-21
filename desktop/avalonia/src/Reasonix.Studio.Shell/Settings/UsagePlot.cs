using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Linq;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Native stacked daily bars with the aspect ratio and hit targets of the reference plot.</summary>
internal sealed class UsagePlot : Control
{
    /// <summary>The daily observations in chart order.</summary>
    private readonly (string Label, double Hit, double Miss)[] _points;

    /// <summary>Initializes a new instance of the <see cref="UsagePlot"/> class.</summary>
    /// <param name="points">Labels and measured values.</param>
    public UsagePlot((string Label, double Hit, double Miss)[] points)
    {
        _points = points;
        PointerMoved += (_, e) =>
        {
            if (_points.Length == 0 || Bounds.Width <= 0)
            {
                return;
            }

            var i = Math.Clamp((int)(((e.GetPosition(this).X / Bounds.Width * 860) - 58) / 790 * _points.Length), 0, _points.Length - 1);
            var p = _points[i];
            ToolTip.SetTip(this, p.Label + " · " + (p.Hit + p.Miss).ToString("N2", System.Globalization.CultureInfo.CurrentCulture));
        };
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width / 860;
        var h = Bounds.Height / 176;
        var max = _points.Select(p => p.Hit + p.Miss).DefaultIfEmpty().Max();
        if (max <= 0 || w <= 0 || h <= 0)
        {
            return;
        }

        var pen = new Pen(Brush("HairBrush"), 1);
        for (var i = 0; i <= 2; i++)
        {
            var y = (12 + (70 * i)) * h;
            context.DrawLine(pen, new Point(58 * w, y), new Point(848 * w, y));
            var value = max * (1 - (i / 2d));
            var text = value >= 1000000 ? (value / 1000000).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + "M" : value.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture);
            Label(text, 0, Math.Max(0, y - 6));
        }

        var step = 790 * w / _points.Length;
        var ink = Brush("BrandBrush");
        var dim = Brush("MutedBrush");
        for (var i = 0; i < _points.Length; i++)
        {
            var point = _points[i];
            var hit = point.Hit / max * 140 * h;
            var miss = point.Miss / max * 140 * h;
            var x = (58 * w) + (i * step);
            context.DrawRectangle(ink, null, new Rect(x, (152 * h) - hit, Math.Max(1, step - 2), hit));
            context.DrawRectangle(dim, null, new Rect(x, (152 * h) - hit - miss, Math.Max(1, step - 2), miss));
        }

        Label(_points[0].Label, 58 * w, 154 * h);
        Label(_points[^1].Label, Math.Max(58 * w, Bounds.Width - 65), 154 * h);

        void Label(string value, double x, double y)
        {
            var text = new FormattedText(value, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), Math.Min(11, 15 * w), Brush("MutedBrush"));
            context.DrawText(text, new Point(x, y));
        }
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 860;
        return new Size(width, Math.Floor(width * 176 / 860 * 64) / 64);
    }

    /// <summary>Resolves ink in the active theme.</summary>
    /// <param name="key">Resource or field name.</param>
    /// <returns>Resolved theme brush.</returns>
    private IBrush Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;
}
