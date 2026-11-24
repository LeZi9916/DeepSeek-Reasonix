using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Draws the diagonal stripes of the project drop target.</summary>
internal sealed class OnboardingStripes : Control
{
    /// <summary>The horizontal position within one stripe tile.</summary>
    public static readonly StyledProperty<double> ShiftProperty = AvaloniaProperty.Register<OnboardingStripes, double>(nameof(Shift));

    /// <summary>The accent brush of the accepted drop state.</summary>
    public static readonly StyledProperty<IBrush?> InkProperty = AvaloniaProperty.Register<OnboardingStripes, IBrush?>(nameof(Ink));

    /// <summary>Initializes static members of the <see cref="OnboardingStripes"/> class.</summary>
    static OnboardingStripes() => AffectsRender<OnboardingStripes>(ShiftProperty, InkProperty);

    /// <summary>Gets or sets the offset of the stripes.</summary>
    public double Shift
    {
        get => GetValue(ShiftProperty);
        set => SetValue(ShiftProperty, value);
    }

    /// <summary>Gets or sets the accent brush of the stripes.</summary>
    public IBrush? Ink
    {
        get => GetValue(InkProperty);
        set => SetValue(InkProperty, value);
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        if (Ink is { } brush)
        {
            var pen = new Pen(brush, 8);
            for (var x = Shift - Bounds.Height - 22.6; x < Bounds.Width + 22.6; x += 22.6)
            {
                context.DrawLine(pen, new Point(x, 0), new Point(x + Bounds.Height, Bounds.Height));
            }
        }
    }
}
