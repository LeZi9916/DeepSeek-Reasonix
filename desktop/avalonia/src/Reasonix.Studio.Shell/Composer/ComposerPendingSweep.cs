using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Reasonix.Studio.Shell.Composer;

/// <summary>Draws the composer tool row's pending sweep below its bottom edge.</summary>
internal sealed class ComposerPendingSweep : Control
{
    /// <summary>The sweep's position or a negative value when the reduced-motion line is shown.</summary>
    public static readonly StyledProperty<double> PhaseProperty =
        AvaloniaProperty.Register<ComposerPendingSweep, double>(nameof(Phase), -1);

    /// <summary>The same network color the shell uses for pending work.</summary>
    public static readonly StyledProperty<IBrush?> InkProperty =
        AvaloniaProperty.Register<ComposerPendingSweep, IBrush?>(nameof(Ink));

    /// <summary>Initializes static members of the <see cref="ComposerPendingSweep"/> class.</summary>
    static ComposerPendingSweep() => AffectsRender<ComposerPendingSweep>(PhaseProperty, InkProperty);

    /// <summary>Gets or sets the travel within the sweep cycle.</summary>
    public double Phase
    {
        get => GetValue(PhaseProperty);
        set => SetValue(PhaseProperty, value);
    }

    /// <summary>Gets or sets the pending-status brush.</summary>
    public IBrush? Ink
    {
        get => GetValue(InkProperty);
        set => SetValue(InkProperty, value);
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Ink is not ISolidColorBrush ink)
        {
            return;
        }

        if (Phase < 0)
        {
            using var opacity = context.PushOpacity(0.5);
            context.FillRectangle(ink, new Rect(Bounds.Size));
            return;
        }

        var color = ink.Color;
        var transparent = Color.FromArgb(0, color.R, color.G, color.B);
        var paint = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = new GradientStops { new(transparent, 0), new(color, 0.5), new(transparent, 1) },
        };
        var width = Bounds.Width * 0.34;
        var x = (Bounds.Width - width) * (-0.34 + (1.68 * Phase));
        context.FillRectangle(paint, new Rect(x, 0, width, Bounds.Height));
    }
}
