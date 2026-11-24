using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Draws the scrolling 42-pixel grid of the reference onboarding stage.</summary>
internal sealed class OnboardingGrid : Control
{
    /// <summary>The distance moved within one full grid tile.</summary>
    public static readonly StyledProperty<double> ShiftProperty = AvaloniaProperty.Register<OnboardingGrid, double>(nameof(Shift));

    /// <summary>The brush drawn behind the onboarding shell.</summary>
    public static readonly StyledProperty<IBrush?> InkProperty = AvaloniaProperty.Register<OnboardingGrid, IBrush?>(nameof(Ink));

    /// <summary>Initializes static members of the <see cref="OnboardingGrid"/> class.</summary>
    static OnboardingGrid() => AffectsRender<OnboardingGrid>(ShiftProperty, InkProperty);

    /// <summary>Gets or sets the current displacement of the grid.</summary>
    public double Shift
    {
        get => GetValue(ShiftProperty);
        set => SetValue(ShiftProperty, value);
    }

    /// <summary>Gets or sets the brush drawn by the grid lines.</summary>
    public IBrush? Ink
    {
        get => GetValue(InkProperty);
        set => SetValue(InkProperty, value);
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        var offset = Shift % 42;
        for (var x = offset; x < Bounds.Width; x += 42)
        {
            context.DrawRectangle(Ink, null, new Rect(x, 0, 1, Bounds.Height));
        }

        for (var y = offset; y < Bounds.Height; y += 42)
        {
            context.DrawRectangle(Ink, null, new Rect(0, y, Bounds.Width, 1));
        }
    }
}
