using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Reasonix.Studio.Controls;

/// <summary>Paints the moving highlight on the composer border.</summary>
public sealed class ComposerSweep : Border
{
    /// <summary>Defines the start angle of the highlight.</summary>
    public static readonly StyledProperty<double> AngleProperty =
        AvaloniaProperty.Register<ComposerSweep, double>(nameof(Angle), 190);

    /// <summary>Defines the focus color used by the highlight.</summary>
    public static readonly StyledProperty<Color> InkProperty =
        AvaloniaProperty.Register<ComposerSweep, Color>(nameof(Ink));

    /// <summary>The gradient painted along the rounded border.</summary>
    private readonly ConicGradientBrush _sweep = new()
    {
        Angle = 190,
        GradientStops =
        {
            new GradientStop(Colors.Transparent, 0.62),
            new GradientStop(Colors.Transparent, 0.82),
            new GradientStop(Colors.Transparent, 1),
        },
    };

    /// <summary>Initializes a new instance of the <see cref="ComposerSweep"/> class.</summary>
    public ComposerSweep() => BorderBrush = _sweep;

    /// <summary>Gets or sets the start angle of the highlight.</summary>
    public double Angle
    {
        get => GetValue(AngleProperty);
        set => SetValue(AngleProperty, value);
    }

    /// <summary>Gets or sets the focus color used by the highlight.</summary>
    public Color Ink
    {
        get => GetValue(InkProperty);
        set => SetValue(InkProperty, value);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AngleProperty)
        {
            _sweep.Angle = Angle;
            InvalidateVisual();
        }
        else if (change.Property == InkProperty)
        {
            var ink = Ink;
            var edge = Color.FromArgb(0, ink.R, ink.G, ink.B);
            _sweep.GradientStops[0].Color = edge;
            _sweep.GradientStops[2].Color = edge;
            _sweep.GradientStops[1].Color = Color.FromArgb((byte)System.Math.Round(ink.A * 0.7), ink.R, ink.G, ink.B);
            InvalidateVisual();
        }
    }
}
