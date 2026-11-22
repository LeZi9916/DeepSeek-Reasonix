using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;

namespace Reasonix.Studio.Controls;

/// <summary>Draws a single CSS line box with half-leading around the primary font's baseline.</summary>
public sealed class StudioLineText : TextBlock
{
    /// <inheritdoc/>
    protected override Type StyleKeyOverride => typeof(TextBlock);

    /// <inheritdoc/>
    protected override void RenderTextLayout(DrawingContext context, Point origin)
    {
        if (double.IsFinite(LineHeight) && TextLayout.TextLines.Count == 1 && FontManager.Current.TryGetGlyphTypeface(new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), out var face))
        {
            var metrics = face.Metrics;
            var scale = FontSize / metrics.DesignEmHeight;
            var baseline = (LineHeight - ((metrics.Ascent + metrics.Descent) * scale)) / 2;
            origin = origin.WithY(origin.Y + baseline - TextLayout.TextLines[0].Baseline);
        }

        base.RenderTextLayout(context, origin);
    }
}
