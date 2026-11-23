using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Draws the preview's outward highlight without changing the sample's layout.</summary>
public sealed class FoldingPreviewFrame : Border
{
    /// <summary>Registers the optional highlight ink.</summary>
    public static readonly StyledProperty<Color> OutlineProperty = AvaloniaProperty.Register<FoldingPreviewFrame, Color>(nameof(Outline));

    /// <summary>Gets or sets the outward highlight ink.</summary>
    public Color Outline
    {
        get => GetValue(OutlineProperty);
        set => SetValue(OutlineProperty, value);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == OutlineProperty)
        {
            var color = Outline;
            BoxShadow = color.A == 0
                ? default
                : new BoxShadows(new BoxShadow
                {
                    Color = Color.FromArgb((byte)System.Math.Round(color.A * 0.35), color.R, color.G, color.B),
                    Spread = 1,
                });
        }
    }
}
