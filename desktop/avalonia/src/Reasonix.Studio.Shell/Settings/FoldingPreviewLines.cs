using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>The geometric lines in the transcript preview's sample text.</summary>
public sealed class FoldingPreviewLines : Control
{
    /// <summary>Registers how many sample lines are drawn.</summary>
    public static readonly StyledProperty<int> CountProperty = AvaloniaProperty.Register<FoldingPreviewLines, int>(nameof(Count), 2);

    /// <summary>Registers the line's height.</summary>
    public static readonly StyledProperty<double> ThicknessProperty = AvaloniaProperty.Register<FoldingPreviewLines, double>(nameof(Thickness), 6);

    /// <summary>Registers the ink used for the sample.</summary>
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<FoldingPreviewLines, IBrush?>(nameof(Fill));

    /// <summary>The line-length pattern repeated in the reference preview.</summary>
    private static readonly double[] s_widths = [0.92, 0.74, 0.86, 0.60, 0.80, 0.48];

    /// <summary>Initializes static members of the <see cref="FoldingPreviewLines"/> class.</summary>
    static FoldingPreviewLines()
    {
        AffectsMeasure<FoldingPreviewLines>(CountProperty, ThicknessProperty);
        AffectsRender<FoldingPreviewLines>(CountProperty, ThicknessProperty, FillProperty);
    }

    /// <summary>Gets or sets the number of sample lines.</summary>
    public int Count
    {
        get => GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }

    /// <summary>Gets or sets the line height.</summary>
    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    /// <summary>Gets or sets the sample color.</summary>
    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        if (Fill is not { } ink)
        {
            return;
        }

        var radius = Thickness == 5 ? 1 : Thickness / 2;
        for (var i = 0; i < Count; i++)
        {
            var width = Bounds.Width * s_widths[i % s_widths.Length];
            context.FillRectangle(ink, new Rect(0, i * (Thickness + 5), width, Thickness), (float)radius);
        }
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize) => new(0, Count > 0 ? (Count * Thickness) + ((Count - 1) * 5) : 0);
}
