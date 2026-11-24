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

    /// <summary>The width transition that opens the docked workbench beside the conversation.</summary>
    private CancellationTokenSource? _workbenchDockEntrance;

    /// <summary>Expands the dock column from the right edge and lets the conversation reflow with it.</summary>
    /// <returns>The opening width transition.</returns>
    private async Task EnterWorkbenchDockAsync()
    {
        var width = WorkbenchPanel.Width;
        _workbenchDockEntrance?.Cancel();
        using var motion = CancellationTokenSource.CreateLinkedTokenSource(_motionLifetime.Token);
        _workbenchDockEntrance = motion;
        Classes.CollectionChanged += MotionChanged;
        try
        {
            var animation = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(260),
                Easing = new SplineEasing(0.32, 0.72, 0, 1),
                FillMode = FillMode.Backward,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(WidthProperty, 0d) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(WidthProperty, width) } },
                },
            };
            await animation.RunAsync(WorkbenchPanel, motion.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (motion.IsCancellationRequested)
        {
        }
        finally
        {
            Classes.CollectionChanged -= MotionChanged;
            if (_workbenchDockEntrance == motion)
            {
                _workbenchDockEntrance = null;
                if (BackgroundJobHost.Content is Workbench.BackgroundJobOutputView job)
                {
                    job.FollowLatestOutput();
                }
            }
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

        if (_workbenchDockEntrance is { } dockEntrance)
        {
            var width = WorkbenchPanel.Bounds.Width;
            dockEntrance.Cancel();
            WorkbenchPanel.Width = width;
        }

        var opacity = WorkbenchPanel.Opacity;
        var offset = WorkbenchPanel.RenderTransform?.Value.M31 ?? 0;
        if (_viewMotion is { View: var view } entrance && view == WorkbenchPanel)
        {
            entrance.Cancel.Cancel();
        }

        using var motion = CancellationTokenSource.CreateLinkedTokenSource(_motionLifetime.Token);
        _workbenchDismiss = motion;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(160),
            Easing = new SplineEasing(0.42, 0, 1, 1),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, opacity), new Setter(TranslateTransform.XProperty, offset) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 0d), new Setter(TranslateTransform.XProperty, 24d) } },
            },
        };

        try
        {
            await animation.RunAsync(WorkbenchPanel, motion.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (motion.IsCancellationRequested)
        {
        }
        finally
        {
            if (_workbenchDismiss == motion)
            {
                _workbenchDismiss = null;
                if (!_closing.IsCancellationRequested)
                {
                    HideWorkbenchAfterDismissal();
                }
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

    /// <summary>Invalidates a dismissal superseded by another navigation.</summary>
    private void CancelWorkbenchDismissal()
    {
        var motion = _workbenchDismiss;
        _workbenchDismiss = null;
        motion?.Cancel();
    }
}
