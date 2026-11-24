using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using System;
using System.Threading;

namespace Reasonix.Studio.Pages;

/// <summary>Clips a retained disclosure while its visible height changes.</summary>
internal sealed class DisclosureMotion : Decorator
{
    /// <summary>Defines whether interaction may animate this body.</summary>
    public static readonly StyledProperty<bool> MotionEnabledProperty =
        AvaloniaProperty.Register<DisclosureMotion, bool>(nameof(MotionEnabled));

    /// <summary>The in-flight change of visible height.</summary>
    private CancellationTokenSource? _transition;

    /// <summary>Gets or sets a value indicating whether interaction may animate this body.</summary>
    public bool MotionEnabled
    {
        get => GetValue(MotionEnabledProperty);
        set => SetValue(MotionEnabledProperty, value);
    }

    /// <summary>Gets or sets the disclosure transition duration in milliseconds.</summary>
    public double DurationMs
    {
        get; set;
    } = 240;

    /// <summary>Gets or sets a value indicating whether to also fade the content.</summary>
    public bool FadeContent
    {
        get; set;
    }

    /// <summary>Gets a value indicating whether the disclosure is open or opening.</summary>
    internal bool Expanded
    {
        get; private set;
    }

    /// <summary>Applies the requested disclosure state.</summary>
    /// <param name="open">Whether the content should be visible.</param>
    /// <param name="animate">Whether the change came from an interactive toggle.</param>
    internal async void SetExpanded(bool open, bool animate = false)
    {
        if (Expanded == open)
        {
            return;
        }

        Expanded = open;
        var fromHeight = !IsVisible ? 0 : double.IsNaN(Height) ? Bounds.Height : Height;
        var fromOpacity = IsVisible ? Opacity : 0;
        _transition?.Cancel();
        _transition = null;
        if (!animate || !MotionEnabled || this.GetVisualParent() is not Control parent || parent.Bounds.Width <= 0 || Child is not { } child)
        {
            Settle();
            return;
        }

        using var motion = new CancellationTokenSource();
        _transition = motion;
        child.Measure(new Size(parent.Bounds.Width, double.PositiveInfinity));
        var toHeight = open ? child.DesiredSize.Height : 0;
        Height = fromHeight;
        if (FadeContent)
        {
            Opacity = fromOpacity;
        }

        IsVisible = true;
        IsHitTestVisible = open;
        var start = new KeyFrame { Cue = new Cue(0), Setters = { new Setter(HeightProperty, fromHeight) } };
        var end = new KeyFrame { Cue = new Cue(1), Setters = { new Setter(HeightProperty, toHeight) } };
        if (FadeContent)
        {
            start.Setters.Add(new Setter(OpacityProperty, fromOpacity));
            end.Setters.Add(new Setter(OpacityProperty, open ? 1d : 0d));
        }

        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(DurationMs),
            Easing = new SplineEasing(0.16, 1, 0.3, 1),
            Children = { start, end },
        };
        try
        {
            await animation.RunAsync(this, motion.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (motion.IsCancellationRequested)
        {
        }
        finally
        {
            if (_transition == motion)
            {
                _transition = null;
                Settle();
            }
        }
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        Child?.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return Child?.DesiredSize ?? default;
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(new Size(finalSize.Width, Child.DesiredSize.Height)));
        return finalSize;
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        CancelMotion();
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MotionEnabledProperty && !MotionEnabled)
        {
            CancelMotion();
        }
    }

    /// <summary>Settles immediately when motion becomes unavailable.</summary>
    private void CancelMotion()
    {
        if (_transition is { } running)
        {
            _transition = null;
            running.Cancel();
            Settle();
        }
    }

    /// <summary>Restores natural layout at the requested endpoint.</summary>
    private void Settle()
    {
        IsVisible = Expanded;
        IsHitTestVisible = Expanded;
        ClearValue(HeightProperty);
        ClearValue(OpacityProperty);
    }
}
