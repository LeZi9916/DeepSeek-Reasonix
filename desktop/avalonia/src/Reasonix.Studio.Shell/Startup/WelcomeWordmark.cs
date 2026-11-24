using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.Linq;

namespace Reasonix.Studio.Shell.Startup;

/// <summary>Draws the welcome wordmark using the reference SVG's full view box.</summary>
internal sealed class WelcomeWordmark : Control
{
    /// <summary>The currently animated ink.</summary>
    private IBrush _ink = Brush.Parse("#0153E5");

    /// <summary>The existing startup outlines at their original position in the source wordmark.</summary>
    private Geometry[]? _shapes;

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        _shapes ??= Enumerable.Range(0, 8).Select(i => (Geometry)this.FindResource($"Boot.Glyph{i}")!).ToArray();
        using var sizing = context.PushTransform(Matrix.CreateScale(Bounds.Width / 1692.66, Bounds.Height / 392.25));
        foreach (var shape in _shapes)
        {
            context.DrawGeometry(_ink, null, shape);
        }
    }

    /// <summary>Changes the brightness of the mark with the same glow that lights it.</summary>
    /// <param name="ink">The current ink.</param>
    internal void Paint(Color ink)
    {
        _ink = new SolidColorBrush(ink);
        InvalidateVisual();
    }
}
