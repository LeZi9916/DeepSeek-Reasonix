using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;

namespace Reasonix.Studio.Shell.Composer;

/// <summary>Interpolates the changing token count using this view's resolved theme colors.</summary>
internal sealed class RunTokenValue : TextBlock
{
    /// <summary>The base ink for this reading, which can include estimated output.</summary>
    public static readonly StyledProperty<IBrush?> StableInkProperty =
        AvaloniaProperty.Register<RunTokenValue, IBrush?>(nameof(StableInk));

    /// <summary>The accent used when a new count lands.</summary>
    public static readonly StyledProperty<IBrush?> HighlightInkProperty =
        AvaloniaProperty.Register<RunTokenValue, IBrush?>(nameof(HighlightInk));

    /// <summary>The fraction of the highlight still visible.</summary>
    public static readonly StyledProperty<double> TintProperty =
        AvaloniaProperty.Register<RunTokenValue, double>(nameof(Tint));

    /// <summary>Gets or sets the settled ink.</summary>
    public IBrush? StableInk
    {
        get => GetValue(StableInkProperty);
        set => SetValue(StableInkProperty, value);
    }

    /// <summary>Gets or sets the highlight ink.</summary>
    public IBrush? HighlightInk
    {
        get => GetValue(HighlightInkProperty);
        set => SetValue(HighlightInkProperty, value);
    }

    /// <summary>Gets or sets the amount of highlight.</summary>
    public double Tint
    {
        get => GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TintProperty || change.Property == StableInkProperty || change.Property == HighlightInkProperty)
        {
            if (Tint > 0 && HighlightInk is ISolidColorBrush highlight && StableInk is ISolidColorBrush stable)
            {
                SetCurrentValue(ForegroundProperty, new SolidColorBrush(Color.FromRgb(
                    Mix(stable.Color.R, highlight.Color.R, Tint),
                    Mix(stable.Color.G, highlight.Color.G, Tint),
                    Mix(stable.Color.B, highlight.Color.B, Tint))));
            }
            else
            {
                SetCurrentValue(ForegroundProperty, StableInk);
            }
        }
    }

    /// <summary>Interpolates one opaque channel as in the reference count tint.</summary>
    /// <param name="baseValue">The settled channel.</param>
    /// <param name="flash">The highlight channel.</param>
    /// <param name="fraction">The amount of highlight.</param>
    /// <returns>The resulting color channel.</returns>
    private static byte Mix(byte baseValue, byte flash, double fraction) => (byte)Math.Round(baseValue + ((flash - baseValue) * fraction));
}
