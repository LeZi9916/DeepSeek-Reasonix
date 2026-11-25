using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The selected surface of the current workbench presentation.</summary>
    private object? _workbenchCanvasSelection;

    /// <summary>The in-flight fade of the selected workbench canvas.</summary>
    private CancellationTokenSource? _workbenchCanvasFade;

    /// <summary>The slide that owns the pending workbench dismissal.</summary>
    private CancellationTokenSource? _workbenchDismiss;

    /// <summary>The current workbench entrance, including whether it animates the dock width.</summary>
    private (CancellationTokenSource Lifetime, bool Docked)? _workbenchEntrance;

    /// <summary>Creates one animation binding to drive the workbench pose.</summary>
    /// <param name="property">Width or opacity.</param><param name="from">Starting value.</param><param name="to">Destination.</param>
    /// <param name="milliseconds">Transition duration.</param><param name="easing">Motion curve.</param><param name="fill">Endpoint handling.</param>
    /// <returns>The animation driving the pose.</returns>
    private static Animation WorkbenchTransition(AvaloniaProperty property, double from, double to, int milliseconds, Easing easing, FillMode fill) =>
        new()
        {
            Duration = TimeSpan.FromMilliseconds(milliseconds),
            Easing = easing,
            FillMode = fill,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(property, from) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(property, to) } },
            },
        };

    /// <summary>Opens the workbench from the current rendered pose.</summary>
    /// <param name="width">The initial dock width, or NaN for an overlay.</param>
    /// <param name="opacity">The visible opacity when opening begins.</param>
    /// <param name="offset">The visible horizontal offset when opening begins.</param>
    /// <returns>The opening transition.</returns>
    private async Task EnterWorkbenchAsync(double width, double opacity, double offset)
    {
        var targetWidth = WorkbenchPanel.Width;
        var docked = !double.IsNaN(targetWidth);
        var expands = docked && !double.IsNaN(width) && width != targetWidth;
        CancelWorkbenchEntrance();
        SetWorkbenchPose(opacity, offset);
        if (!expands && opacity == 1 && offset == 0)
        {
            return;
        }

        using var motion = CancellationTokenSource.CreateLinkedTokenSource(_motionLifetime.Token);
        _workbenchEntrance = (motion, docked);
        var property = expands ? WidthProperty : OpacityProperty;
        var from = expands ? width : opacity;
        var to = expands ? targetWidth : 1;
        var easing = docked ? new SplineEasing(0.32, 0.72, 0, 1) : new SplineEasing(0.2, 0, 0, 1);
        var animation = WorkbenchTransition(property, from, to, docked ? 260 : 200, easing, expands ? FillMode.Backward : FillMode.Forward);

        WorkbenchPanel.PropertyChanged += FollowOpening;
        try
        {
            await PlayWorkbenchVisibilityAsync(animation, motion).ConfigureAwait(true);
        }
        finally
        {
            WorkbenchPanel.PropertyChanged -= FollowOpening;
            if (_workbenchEntrance?.Lifetime == motion)
            {
                _workbenchEntrance = null;
                SetWorkbenchPose(1, 0);
                if (!_closing.IsCancellationRequested)
                {
                    LayoutWorkbench();
                    if (docked && BackgroundJobHost.Content is Workbench.BackgroundJobOutputView job)
                    {
                        job.FollowLatestOutput();
                    }
                }
            }
        }

        void FollowOpening(object? sender, AvaloniaPropertyChangedEventArgs change)
        {
            if (_workbenchEntrance?.Lifetime != motion || change.Property != property)
            {
                return;
            }

            var current = expands ? WorkbenchPanel.Width : WorkbenchPanel.Opacity;
            var progress = to == from ? 1 : Math.Clamp((current - from) / (to - from), 0, 1);
            if (expands && opacity != 1)
            {
                WorkbenchPanel.Opacity = double.Lerp(opacity, 1, progress);
            }

            if (offset != 0)
            {
                ((TranslateTransform)WorkbenchPanel.RenderTransform!).X = double.Lerp(offset, 0, progress);
            }
        }
    }

    /// <summary>Plays a workbench transition until completion, replacement or reduced motion.</summary>
    /// <param name="animation">The transition between visible poses.</param>
    /// <param name="motion">The lifetime of this transition.</param>
    /// <returns>The render transition.</returns>
    private async Task PlayWorkbenchVisibilityAsync(Animation animation, CancellationTokenSource motion)
    {
        Classes.CollectionChanged += MotionChanged;
        try
        {
            await animation.RunAsync(WorkbenchPanel, motion.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (motion.IsCancellationRequested)
        {
        }
        finally
        {
            Classes.CollectionChanged -= MotionChanged;
        }

        void MotionChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (!Classes.Contains("motion"))
            {
                motion.Cancel();
            }
        }
    }

    /// <summary>Fades the canvas when a different surface becomes selected.</summary>
    /// <param name="selected">The canonical key of the selected surface.</param>
    /// <param name="animate">Whether the selection happened in the current workbench.</param>
    private async void UpdateWorkbenchCanvas(object? selected, bool animate)
    {
        if (Equals(_workbenchCanvasSelection, selected))
        {
            return;
        }

        _workbenchCanvasSelection = selected;
        _workbenchCanvasFade?.Cancel();
        if (!animate || selected is null or "launcher:" || !WorkbenchPanel.IsEffectivelyVisible || !Classes.Contains("motion") || _closing.IsCancellationRequested)
        {
            return;
        }

        using var fade = CancellationTokenSource.CreateLinkedTokenSource(_motionLifetime.Token);
        _workbenchCanvasFade = fade;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(120),
            Easing = new SplineEasing(0, 0, 0.58, 1),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0.35d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d) } },
            },
        };
        try
        {
            var surface = SubagentRunHost.IsVisible ? (Avalonia.Controls.Control)SubagentRunHost : BackgroundJobHost.IsVisible ? BackgroundJobHost : ManualBrowserHost.IsVisible ? ManualBrowserHost : FileCanvas;
            await animation.RunAsync(surface, fade.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (fade.IsCancellationRequested)
        {
        }
        finally
        {
            if (_workbenchCanvasFade == fade)
            {
                _workbenchCanvasFade = null;
            }
        }
    }

    /// <summary>Slides the workbench out before returning space and focus to the conversation.</summary>
    /// <returns>The dismissal of the workbench.</returns>
    private async Task DismissWorkbenchAsync()
    {
        if (!WorkbenchPanel.IsVisible || _workbenchDismiss is not null)
        {
            return;
        }

        if (!Classes.Contains("motion"))
        {
            HideWorkbenchAfterDismissal();
            return;
        }

        var width = WorkbenchPanel.Width;
        var opacity = WorkbenchPanel.Opacity;
        var offset = ReadWorkbenchOffset();
        CancelWorkbenchEntrance();
        WorkbenchPanel.Width = width;
        SetWorkbenchPose(opacity, offset);

        using var motion = CancellationTokenSource.CreateLinkedTokenSource(_motionLifetime.Token);
        _workbenchDismiss = motion;
        var animation = WorkbenchTransition(OpacityProperty, opacity, 0, 160, new SplineEasing(0.42, 0, 1, 1), FillMode.Forward);
        WorkbenchPanel.PropertyChanged += FollowOpacity;
        try
        {
            await PlayWorkbenchVisibilityAsync(animation, motion).ConfigureAwait(true);
        }
        finally
        {
            WorkbenchPanel.PropertyChanged -= FollowOpacity;
            if (_workbenchDismiss == motion)
            {
                _workbenchDismiss = null;
                if (!_closing.IsCancellationRequested)
                {
                    HideWorkbenchAfterDismissal();
                }
            }
        }

        void FollowOpacity(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (_workbenchDismiss == motion && args.Property == OpacityProperty)
            {
                var remaining = opacity > 0 ? WorkbenchPanel.Opacity / opacity : 0;
                ((TranslateTransform)WorkbenchPanel.RenderTransform!).X = double.Lerp(24, offset, remaining);
            }
        }
    }

    /// <summary>Restores the conversation after an explicit workbench dismissal.</summary>
    private void HideWorkbenchAfterDismissal()
    {
        DetachTerminal();
        WorkbenchPanel.IsVisible = false;
        LayoutWorkbench();
        if (Composer.IsEffectivelyVisible)
        {
            Composer.Focus();
        }
    }

    /// <summary>Reads the current horizontal displacement before an animation is replaced.</summary>
    /// <returns>The offset in device-independent pixels.</returns>
    private double ReadWorkbenchOffset() => ((TranslateTransform)WorkbenchPanel.RenderTransform!).X;

    /// <summary>Writes the base pose for a steady or interrupted workbench.</summary>
    /// <param name="opacity">The workbench opacity.</param><param name="offset">Horizontal position.</param>
    private void SetWorkbenchPose(double opacity, double offset)
    {
        WorkbenchPanel.Opacity = opacity;
        ((TranslateTransform)WorkbenchPanel.RenderTransform!).X = offset;
    }

    /// <summary>Invalidates an entrance superseded by dismissal or navigation.</summary>
    private void CancelWorkbenchEntrance()
    {
        var motion = _workbenchEntrance;
        _workbenchEntrance = null;
        motion?.Lifetime.Cancel();
    }

    /// <summary>Invalidates a dismissal superseded by another navigation.</summary>
    private void CancelWorkbenchDismissal()
    {
        var motion = _workbenchDismiss;
        _workbenchDismiss = null;
        if (motion is not null)
        {
            motion.Cancel();
            SetWorkbenchPose(1, 0);
        }
    }
}
