using Avalonia;
using Avalonia.Controls;
using System;

namespace Reasonix.Studio.Controls;

/// <summary>Switches visible native surfaces while retaining their visual attachment and browser state.</summary>
public sealed class StudioSurfaceHost : Grid
{
    /// <summary>Defines the selected retained surface.</summary>
    public static readonly StyledProperty<Control?> ContentProperty = AvaloniaProperty.Register<StudioSurfaceHost, Control?>(nameof(Content));

    /// <summary>Gets or sets the selected surface without detaching its inactive siblings.</summary>
    public Control? Content
    {
        get => GetValue(ContentProperty); set => SetValue(ContentProperty, value);
    }

    /// <summary>Releases a surface after an explicit close.</summary>
    /// <param name="surface">The closed surface.</param>
    public void Release(Control surface)
    {
        if (ReferenceEquals(Content, surface))
        {
            Content = null;
        }

        Children.Remove(surface);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ContentProperty)
        {
            return;
        }

        if (Content is { } selected && !Children.Contains(selected))
        {
            Children.Add(selected);
        }

        foreach (var surface in Children)
        {
            surface.IsVisible = ReferenceEquals(surface, Content);
        }
    }
}
