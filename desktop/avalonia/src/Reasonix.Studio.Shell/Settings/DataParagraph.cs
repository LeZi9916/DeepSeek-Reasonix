using Avalonia;
using Avalonia.Controls;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Preserves fractional CSS line heights as settings text wraps under native font shaping.</summary>
internal sealed class DataParagraph : TextBlock
{
    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        var measured = base.MeasureOverride(availableSize);
        return new Size(measured.Width, TextLayout.TextLines.Count * LineHeight);
    }
}
