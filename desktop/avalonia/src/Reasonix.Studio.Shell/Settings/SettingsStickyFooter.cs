using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using System;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Pins settings actions within their form while the outer settings page scrolls.</summary>
internal sealed class SettingsStickyFooter : Border
{
    /// <summary>The topmost control the footer may overlap.</summary>
    public static readonly StyledProperty<Control?> AnchorProperty =
        AvaloniaProperty.Register<SettingsStickyFooter, Control?>(nameof(Anchor));

    /// <summary>The native offset that preserves the footer's position in layout.</summary>
    private readonly TranslateTransform _offset = new();

    /// <summary>The outer settings viewport.</summary>
    private ScrollViewer? _viewport;

    /// <summary>Initializes a new instance of the <see cref="SettingsStickyFooter"/> class.</summary>
    public SettingsStickyFooter() => RenderTransform = _offset;

    /// <summary>Gets or sets the upper limit of the pinned footer.</summary>
    public Control? Anchor
    {
        get => GetValue(AnchorProperty);
        set => SetValue(AnchorProperty, value);
    }

    /// <inheritdoc/>
    protected override Type StyleKeyOverride => typeof(Border);

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _viewport = this.FindAncestorOfType<ScrollViewer>();
        if (_viewport is not null)
        {
            _viewport.ScrollChanged += UpdateOffset;
            LayoutUpdated += UpdateOffset;
        }
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_viewport is not null)
        {
            _viewport.ScrollChanged -= UpdateOffset;
            LayoutUpdated -= UpdateOffset;
            _viewport = null;
        }

        _offset.Y = 0;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Clamps the position between the form's first field and natural footer.</summary>
    /// <param name="sender">The scrolling or laid-out element.</param>
    /// <param name="e">The change.</param>
    private void UpdateOffset(object? sender, EventArgs e)
    {
        if (_viewport is not { } viewport || !IsEffectivelyVisible || Anchor is not { IsVisible: true } anchor
            || this.TranslatePoint(default, viewport) is not { } position || anchor.TranslatePoint(default, viewport) is not { } start)
        {
            _offset.Y = 0;
            return;
        }

        var top = position.Y - _offset.Y;
        var travel = Math.Max(0, top - start.Y);
        _offset.Y = Math.Clamp(viewport.Viewport.Height - top - Bounds.Height, -travel, 0);
    }
}
