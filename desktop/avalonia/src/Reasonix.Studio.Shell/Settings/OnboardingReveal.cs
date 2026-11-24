using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Reveals the wordmark from the left while retaining its full size.</summary>
internal sealed class OnboardingReveal : Decorator
{
    /// <summary>Defines the visible portion of the wordmark.</summary>
    public static readonly StyledProperty<double> FractionProperty =
        AvaloniaProperty.Register<OnboardingReveal, double>(nameof(Fraction), 1);

    /// <summary>The clip that follows the wordmark's current size.</summary>
    private readonly RectangleGeometry _reveal = new();

    /// <summary>Initializes a new instance of the <see cref="OnboardingReveal"/> class.</summary>
    public OnboardingReveal() => Clip = _reveal;

    /// <summary>Gets or sets the visible portion of the wordmark.</summary>
    public double Fraction
    {
        get => GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FractionProperty || change.Property == BoundsProperty)
        {
            _reveal.Rect = new Rect(0, 0, Bounds.Width * Math.Clamp(Fraction, 0, 1), Bounds.Height);
        }
    }
}
