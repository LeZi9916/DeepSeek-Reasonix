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

namespace Reasonix.Studio.Shell.Composer;

internal sealed partial class BackgroundJobChip
{
    /// <summary>The jobs whose deck row has already entered.</summary>
    private readonly HashSet<string> _jobRendered = new(StringComparer.Ordinal);

    /// <summary>Plays the reference card entrance when a job first appears in the deck.</summary>
    /// <param name="row">The new job row.</param>
    private void EnterJobRow(Border row)
    {
        if (TopLevel.GetTopLevel(this) is not Window window || !window.Classes.Contains("motion"))
        {
            return;
        }

        row.RenderTransform = new TranslateTransform();
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(180),
            Easing = new SplineEasing(0.16, 1, 0.3, 1),
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
        _ = PlayJobMotion(row, animation);
    }

    /// <summary>Runs a job entrance until it settles or its row leaves the deck.</summary>
    /// <param name="row">The job row.</param>
    /// <param name="animation">The entrance animation.</param>
    /// <returns>The completed entrance.</returns>
    private async Task PlayJobMotion(Border row, Animation animation)
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
        }
    }
}
