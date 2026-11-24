using Avalonia;
using Avalonia.Controls;

namespace Reasonix.Studio.Controls;

/// <summary>Sizes one child from the width it is given and a fixed width-to-height ratio.</summary>
public sealed class AspectRatioView : Decorator
{
    /// <summary>Gets or sets the width divided by the height.</summary>
    public double Ratio { get; set; } = 1.6;

    /// <summary>Gets or sets the width used when the parent offers an unbounded one.</summary>
    public double FallbackWidth { get; set; } = 190;

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) && availableSize.Width > 0
            ? availableSize.Width
            : FallbackWidth;
        var ratio = double.IsFinite(Ratio) && Ratio > 0 ? Ratio : 1.6;
        var size = new Size(width, width / ratio);
        Child?.Measure(size);
        return size;
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(finalSize));
        return finalSize;
    }
}
