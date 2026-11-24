using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using System;

namespace Reasonix.Studio.Shell.Startup;

/// <summary>Draws the original title-bar highlight while the first page arrives.</summary>
internal sealed class BootChromeSweep : Control
{
    /// <summary>The position in the 1.15-second chrome sweep.</summary>
    public static readonly StyledProperty<double> PhaseProperty = AvaloniaProperty.Register<BootChromeSweep, double>(nameof(Phase), 1);

    /// <summary>The chrome accent mixed into the transparency gradient.</summary>
    public static readonly StyledProperty<IBrush?> InkProperty = AvaloniaProperty.Register<BootChromeSweep, IBrush?>(nameof(Ink));

    /// <summary>The easing used by the original title-bar sweep.</summary>
    private static readonly SplineEasing s_out = new(0.16, 1, 0.3, 1);

    /// <summary>Initializes static members of the <see cref="BootChromeSweep"/> class.</summary>
    static BootChromeSweep() => AffectsRender<BootChromeSweep>(PhaseProperty, InkProperty);

    /// <summary>Gets or sets the sweep progress.</summary>
    public double Phase
    {
        get => GetValue(PhaseProperty);
        set => SetValue(PhaseProperty, value);
    }

    /// <summary>Gets or sets the accent brush.</summary>
    public IBrush? Ink
    {
        get => GetValue(InkProperty);
        set => SetValue(InkProperty, value);
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Phase is <= 0 or >= 1 || Ink is not ISolidColorBrush accent)
        {
            return;
        }

        var position = BootSky.Mix(2.1, -0.6, s_out.Ease(Phase));
        var opacity = Phase < 0.16 ? s_out.Ease(Phase / 0.16) : 1 - s_out.Ease((Phase - 0.16) / 0.84);
        var backgroundWidth = Bounds.Width * 2.2;
        var center = new Point(((Bounds.Width - backgroundWidth) * position) + (backgroundWidth / 2), Bounds.Height / 2);
        var direction = new Vector(Math.Sin(105 * Math.PI / 180), -Math.Cos(105 * Math.PI / 180));
        var span = (backgroundWidth * direction.X) + (Bounds.Height * direction.Y);
        var half = direction * (span / 2);
        var color = accent.Color;
        var clear = Color.FromArgb(0, color.R, color.G, color.B);
        var wash = BootSky.Ink(color.R, color.G, color.B, 0.3 * opacity);
        var paint = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(center - half, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(center + half, RelativeUnit.Absolute),
            GradientStops = new GradientStops { new(clear, 0.34), new(wash.Color, 0.5), new(clear, 0.66) },
        };
        context.FillRectangle(paint, new Rect(Bounds.Size));
    }
}
