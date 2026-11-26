using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using System;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Pages;

internal sealed partial class ToolCallHeader
{
    /// <summary>The current explicit output-height transition.</summary>
    private CancellationTokenSource? _outputClipMotion;

    /// <summary>Slides a newly dispatched activity row into view.</summary>
    /// <param name="order">The index among calls dispatched together.</param>
    internal void EnterActivity(int order)
    {
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(180),
            Delay = TimeSpan.FromMilliseconds(Math.Min(7, order) * 36),
            FillMode = FillMode.Backward,
            Easing = new SplineEasing(0.16, 1, 0.3, 1),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d), new Setter(TranslateTransform.YProperty, 4d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d), new Setter(TranslateTransform.YProperty, 0d) } },
            },
        };
        _ = PlayActivityMotion(this, animation);
    }

    /// <summary>Plays the corresponding completion or failure motion on a live change.</summary>
    /// <param name="failed">Whether the tool just failed.</param>
    /// <param name="returned">Whether the tool changed from running to settled.</param>
    internal void SettleActivity(bool failed, bool returned)
    {
        if (returned)
        {
            SwapActivityHeading();
        }

        if (failed)
        {
            var frames = new Animation { Duration = TimeSpan.FromMilliseconds(180) };
            var offsets = new[] { 0d, -2d, 2d, -1d, 0d };
            for (var i = 0; i < offsets.Length; i++)
            {
                frames.Children.Add(new KeyFrame
                {
                    Cue = new Cue(i / 4d),
                    KeySpline = new KeySpline(0.25, 0.1, 0.25, 1),
                    Setters = { new Setter(TranslateTransform.XProperty, offsets[i]) },
                });
            }

            _ = PlayActivityMotion(this, frames);
            return;
        }

        if (returned)
        {
            var kind = this.FindControl<Viewbox>("ToolKind")!;
            kind.Classes.Set("done", true);
            DispatcherTimer.RunOnce(() => kind.Classes.Set("done", false), TimeSpan.FromMilliseconds(260));
        }

        var pop = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(160),
            Children = { ScaleAt(0, 0), ScaleAt(0.7, 1.25), ScaleAt(1, 1) },
        };
        _ = PlayActivityMotion(this.FindControl<Viewbox>("StaticStatus")!, pop);

        static KeyFrame ScaleAt(double cue, double scale) => new()
        {
            Cue = new Cue(cue),
            KeySpline = new KeySpline(0.34, 1.56, 0.64, 1),
            Setters = { new Setter(ScaleTransform.ScaleXProperty, scale), new Setter(ScaleTransform.ScaleYProperty, scale) },
        };
    }

    /// <summary>Stops drawing the animation once the source row leaves the transcript.</summary>
    /// <param name="target">The visible header or status glyph.</param>
    /// <param name="animation">The live row animation.</param>
    /// <returns>The duration of the effect.</returns>
    private static async Task PlayActivityMotion(Control target, Animation animation)
    {
        using var motion = new CancellationTokenSource();
        var window = TopLevel.GetTopLevel(target) as Window;
        if (window is not null)
        {
            window.Classes.CollectionChanged += MotionChanged;
        }

        target.DetachedFromVisualTree += Cancel;
        try
        {
            await animation.RunAsync(target, motion.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (motion.IsCancellationRequested)
        {
        }
        finally
        {
            target.DetachedFromVisualTree -= Cancel;
            if (window is not null)
            {
                window.Classes.CollectionChanged -= MotionChanged;
            }
        }

        void Cancel(object? sender, VisualTreeAttachmentEventArgs e) => motion.Cancel();

        void MotionChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (window?.Classes.Contains("motion") == false)
            {
                motion.Cancel();
            }
        }
    }

    /// <summary>Slides the output viewport between its clipped and full heights.</summary>
    /// <returns>The current output transition.</returns>
    private async Task AnimateOutputClipAsync()
    {
        var prior = _outputClipMotion;
        var from = prior is null ? _output.Bounds.Height : _output.MaxHeight;
        _outputClipMotion = null;
        _output.MaxHeight = from;
        prior?.Cancel();

        var target = _openOutput ? double.PositiveInfinity : _cap;
        if (TopLevel.GetTopLevel(this) is not Window window || !window.Classes.Contains("motion") || from <= 0)
        {
            _output.MaxHeight = target;
            return;
        }

        using var motion = new CancellationTokenSource();
        _outputClipMotion = motion;
        var height = Math.Min(target, Math.Max(_output.Extent.Height, _output.Viewport.Height));
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(120),
            Easing = new SplineEasing(0, 0, 0.58, 1),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(MaxHeightProperty, from) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(MaxHeightProperty, height) } },
            },
        };
        window.Classes.CollectionChanged += MotionChanged;
        DetachedFromVisualTree += Cancel;
        try
        {
            await animation.RunAsync(_output, motion.Token).ConfigureAwait(true);
        }
        finally
        {
            window.Classes.CollectionChanged -= MotionChanged;
            DetachedFromVisualTree -= Cancel;
            if (_outputClipMotion == motion)
            {
                _outputClipMotion = null;
                _output.MaxHeight = _openOutput ? double.PositiveInfinity : _cap;
            }
        }

        void Cancel(object? sender, VisualTreeAttachmentEventArgs args) => motion.Cancel();

        void MotionChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (!window.Classes.Contains("motion"))
            {
                motion.Cancel();
            }
        }
    }

    /// <summary>Reproduces the painted fade and reversal during a tool handoff.</summary>
    private void SwapActivityHeading()
    {
        // At 60 Hz the old 90 ms swap reverses after four painted frames of its 120 ms exit curve.
        // Normalize that visible spline segment and shorten the reverse to the distance covered.
        const double Swap = 200d / 3;
        const double Total = 87.026579;
        var fade = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(Total),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 1d) } },
                new KeyFrame { Cue = new Cue(Swap / Total), KeySpline = new KeySpline(0.369276, 0, 0.713508, 0.369019), Setters = { new Setter(OpacityProperty, 0.830334d) } },
                new KeyFrame { Cue = new Cue(1), KeySpline = new KeySpline(0.55, 0, 1, 0.45), Setters = { new Setter(OpacityProperty, 1d) } },
            },
        };
        _ = PlayActivityMotion(this.FindControl<Button>("Toggle")!, fade);
    }
}
