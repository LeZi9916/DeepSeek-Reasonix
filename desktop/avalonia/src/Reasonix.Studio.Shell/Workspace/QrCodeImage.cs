using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Reasonix.Studio.Shell.Workspace;

/// <summary>Draws the hub-issued SVG QR geometry directly in native Avalonia.</summary>
public sealed class QrCodeImage : Control
{
    /// <summary>The parsed geometry supplied by the sharing endpoint.</summary>
    private (Geometry Shape, IBrush Brush)[] _shapes = Array.Empty<(Geometry, IBrush)>();

    /// <summary>The SVG view-box side length.</summary>
    private double _side = 1;

    /// <summary>Shows the returned SVG path and rectangle geometry.</summary>
    /// <param name="svg">The canonical QR document.</param>
    public void Show(string svg)
    {
        if (svg.Length == 0)
        {
            _shapes = Array.Empty<(Geometry, IBrush)>();
            InvalidateVisual();
            return;
        }

        var root = XElement.Parse(svg);
        var view = ((string?)root.Attribute("viewBox"))?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _side = view?.Length == 4 ? double.Parse(view[2], CultureInfo.InvariantCulture) : double.Parse((string?)root.Attribute("width") ?? "1", CultureInfo.InvariantCulture);
        _shapes = root.Descendants().Where(node => node.Name.LocalName is "path" or "rect").Select(node =>
        {
            double Number(string name) => double.Parse((string?)node.Attribute(name) ?? "0", CultureInfo.InvariantCulture);
            Geometry shape = node.Name.LocalName == "path"
                ? Geometry.Parse((string?)node.Attribute("d") ?? string.Empty)
                : new RectangleGeometry(new Rect(Number("x"), Number("y"), Number("width"), Number("height")));
            IBrush brush = string.Equals((string?)node.Attribute("fill"), "white", StringComparison.OrdinalIgnoreCase) || (string?)node.Attribute("fill") == "#fff" ? Brushes.White : Brushes.Black;
            return (shape, brush);
        }).ToArray();
        InvalidateVisual();
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        context.DrawRectangle(Brushes.White, null, Bounds.WithX(0).WithY(0));
        using (context.PushTransform(Matrix.CreateScale(Bounds.Width / _side, Bounds.Height / _side)))
        {
            foreach (var (shape, brush) in _shapes)
            {
                context.DrawGeometry(brush, null, shape);
            }
        }
    }
}
