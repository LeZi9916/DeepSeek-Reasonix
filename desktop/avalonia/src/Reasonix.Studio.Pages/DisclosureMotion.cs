using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Pages;

/// <summary>Clips a retained disclosure while its visible height changes.</summary>
internal sealed class DisclosureMotion : Decorator
{
    /// <summary>Defines whether interaction may animate this body.</summary>
    public static readonly StyledProperty<bool> MotionEnabledProperty =
        AvaloniaProperty.Register<DisclosureMotion, bool>(nameof(MotionEnabled));

    /// <summary>Fraction of the measured content currently revealed.</summary>
    private static readonly StyledProperty<double> s_progress = AvaloniaProperty.Register<DisclosureMotion, double>("Progress", 1);

    /// <summary>The in-flight change of visible height.</summary>
    private CancellationTokenSource? _transition;

    /// <summary>The end of the current interpolation on the monotonic clock.</summary>
    private long _until;

    /// <summary>Retains the outgoing clip through the replacement's initial layout.</summary>
    private double? _handoffHeight;

    /// <summary>Initializes static members of the <see cref="DisclosureMotion"/> class.</summary>
    static DisclosureMotion() => AffectsMeasure<DisclosureMotion>(s_progress);

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

    /// <summary>Gets or sets the easing used for both disclosure directions.</summary>
    public Easing Easing { get; set; } = new SplineEasing(0.16, 1, 0.3, 1);

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
    internal void SetExpanded(bool open, bool animate = false)
    {
        if (Expanded == open)
        {
            return;
        }

        Expanded = open;
        var progress = IsVisible ? GetValue(s_progress) : 0;
        _handoffHeight = null;
        var old = _transition;
        _transition = null;
        SetValue(s_progress, progress);
        old?.Cancel();

        if (!animate || !MotionEnabled || this.GetVisualParent() is not Control { Bounds.Width: > 0 } || Child is null)
        {
            Settle();
            return;
        }

        _ = PlayAsync(progress, TimeSpan.FromMilliseconds(DurationMs));
    }

    /// <summary>Captures only in-flight motion, to be continued by a replacement control.</summary>
    /// <returns>The current pose while its time window is live.</returns>
    internal Pose? CapturePose() => _transition is null
        ? null
        : new Pose(Expanded, GetValue(s_progress), Bounds.Height, TimeSpan.FromMilliseconds(
            Math.Max(1, Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), _until).TotalMilliseconds)));

    /// <summary>Installs the captured fraction before the replacement is first measured.</summary>
    /// <param name="pose">The still-running pose from the same record.</param>
    internal void RestorePose(Pose pose)
    {
        if (Expanded != pose.Open)
        {
            return;
        }

        _handoffHeight = pose.Height;
        SetValue(s_progress, pose.Progress);
        if (FadeContent)
        {
            Opacity = pose.Progress;
        }

        IsVisible = true;
        IsHitTestVisible = pose.Open;
        AttachedToVisualTree += Resume;

        void Resume(object? sender, VisualTreeAttachmentEventArgs args)
        {
            AttachedToVisualTree -= Resume;
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (_handoffHeight is null || _transition is not null || Expanded != pose.Open)
                    {
                        return;
                    }

                    _handoffHeight = null;
                    InvalidateMeasure();
                    if (this.IsAttachedToVisualTree() && MotionEnabled)
                    {
                        _ = PlayAsync(pose.Progress, pose.Remaining);
                    }
                    else
                    {
                        Settle();
                    }
                },
                DispatcherPriority.Loaded);
        }
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        Child?.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        var desired = Child?.DesiredSize ?? default;
        return desired.WithHeight(_handoffHeight ?? (desired.Height * GetValue(s_progress)));
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
        if (change.Property == s_progress && FadeContent)
        {
            Opacity = GetValue(s_progress);
        }

        if (change.Property == MotionEnabledProperty && !MotionEnabled)
        {
            CancelMotion();
        }
    }

    /// <summary>Owns the visible fraction until the requested endpoint is settled.</summary>
    /// <param name="from">The visible fraction at the handoff.</param>
    /// <param name="duration">The interval left for this disclosure.</param>
    /// <returns>The owned transition.</returns>
    private async Task PlayAsync(double from, TimeSpan duration)
    {
        using var motion = new CancellationTokenSource();
        _transition = motion;
        _until = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);
        SetValue(s_progress, from);
        if (FadeContent)
        {
            Opacity = from;
        }

        IsVisible = true;
        IsHitTestVisible = Expanded;
        var animation = new Animation
        {
            Duration = duration,
            Easing = Easing,
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(s_progress, from) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(s_progress, Expanded ? 1d : 0d) } },
            },
        };
        try
        {
            await animation.RunAsync(this, motion.Token).ConfigureAwait(true);
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
        _handoffHeight = null;
        IsVisible = Expanded;
        IsHitTestVisible = Expanded;
        ClearValue(s_progress);
        ClearValue(OpacityProperty);
    }

    /// <summary>The interrupted visible portion of a disclosure.</summary>
    /// <param name="Open">The accepted fold state.</param><param name="Progress">The visible fraction.</param>
    /// <param name="Height">The measured clip before the replacement.</param><param name="Remaining">The animation time left.</param>
    internal readonly record struct Pose(bool Open, double Progress, double Height, TimeSpan Remaining);
}
