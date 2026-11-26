using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The queue entries whose row has already entered.</summary>
    private readonly HashSet<string> _queueRendered = new(StringComparer.Ordinal);

    /// <summary>Plays the reference card entrance when a queue entry first appears.</summary>
    /// <param name="row">The new queue row.</param>
    private void EnterQueueRow(Control row)
    {
        if (!Classes.Contains("motion"))
        {
            return;
        }

        row.Opacity = 0;
        row.RenderTransform = new TranslateTransform(0, 6);

        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(180),
            Easing = new SplineEasing(0.16, 1, 0.3, 1),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters = { new Setter(OpacityProperty, 0d), new Setter(TranslateTransform.YProperty, 6d) },
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters = { new Setter(OpacityProperty, 1d), new Setter(TranslateTransform.YProperty, 0d) },
                },
            },
        };
        _ = PlayQueueMotion(row, animation);
    }

    /// <summary>Runs a queue entrance until it settles or its row leaves the strip.</summary>
    /// <param name="row">The queue row.</param>
    /// <param name="animation">The entrance animation.</param>
    /// <returns>The completed entrance.</returns>
    private async Task PlayQueueMotion(Control row, Animation animation)
    {
        using var lifetime = new CancellationTokenSource();
        void Detached(object? sender, VisualTreeAttachmentEventArgs args) => lifetime.Cancel();
        row.DetachedFromVisualTree += Detached;
        try
        {
            await animation.RunAsync(row, lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            row.DetachedFromVisualTree -= Detached;
            row.Opacity = 1;
            ((TranslateTransform)row.RenderTransform!).Y = 0;
        }
    }
}
