using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Reasonix.Studio.Pages;

/// <summary>Provides a moving gradient that can be painted inside the text glyphs.</summary>
internal sealed class SweepTextBlock : TextBlock
{
    /// <summary>Defines the start of the gradient relative to the text width.</summary>
    public static readonly StyledProperty<double> SweepPositionProperty =
        AvaloniaProperty.Register<SweepTextBlock, double>(nameof(SweepPosition), -2.25);

    /// <summary>Defines the text color outside the highlight.</summary>
    public static readonly StyledProperty<Color> SweepBaseProperty =
        AvaloniaProperty.Register<SweepTextBlock, Color>(nameof(SweepBase));

    /// <summary>Defines the color at the center of the highlight.</summary>
    public static readonly StyledProperty<Color> SweepHighlightProperty =
        AvaloniaProperty.Register<SweepTextBlock, Color>(nameof(SweepHighlight));

    /// <summary>Initializes static members of the <see cref="SweepTextBlock"/> class.</summary>
    static SweepTextBlock() => AffectsRender<SweepTextBlock>(SweepPositionProperty, SweepBaseProperty, SweepHighlightProperty);

    /// <summary>Gets or sets the start of the gradient relative to the text width.</summary>
    public double SweepPosition
    {
        get => GetValue(SweepPositionProperty);
        set => SetValue(SweepPositionProperty, value);
    }

    /// <summary>Gets or sets the text color outside the highlight.</summary>
    public Color SweepBase
    {
        get => GetValue(SweepBaseProperty);
        set => SetValue(SweepBaseProperty, value);
    }

    /// <summary>Gets or sets the color at the center of the highlight.</summary>
    public Color SweepHighlight
    {
        get => GetValue(SweepHighlightProperty);
        set => SetValue(SweepHighlightProperty, value);
    }

    /// <summary>Gets the per-label foreground brush for the sweep.</summary>
    public LinearGradientBrush SweepBrush
    {
        get;
    } = new()
    {
        StartPoint = new RelativePoint(-2.25, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0.25, 0, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(default, 0.4),
            new GradientStop(default, 0.5),
            new GradientStop(default, 0.6),
        },
    };

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SweepPositionProperty)
        {
            SweepBrush.StartPoint = new RelativePoint(SweepPosition, 0, RelativeUnit.Relative);
            SweepBrush.EndPoint = new RelativePoint(SweepPosition + 2.5, 0, RelativeUnit.Relative);
        }
        else if (change.Property == SweepBaseProperty)
        {
            SweepBrush.GradientStops[0].Color = SweepBase;
            SweepBrush.GradientStops[2].Color = SweepBase;
        }
        else if (change.Property == SweepHighlightProperty)
        {
            SweepBrush.GradientStops[1].Color = SweepHighlight;
        }
    }
}
