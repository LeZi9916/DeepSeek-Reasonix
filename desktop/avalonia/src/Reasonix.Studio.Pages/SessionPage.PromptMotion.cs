using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Pages;

public sealed partial class SessionPage
{
    /// <summary>Plays the reference right-to-left entrance for a newly arrived approval or question.</summary>
    /// <param name="previous">The state before the prompt arrived.</param>
    private void AnimatePromptEntrances(SessionState previous)
    {
        if (TopLevel.GetTopLevel(this) is not Window window || !window.Classes.Contains("motion"))
        {
            return;
        }

        var before = previous.Items.Where(item => item is ApprovalItem or AskItem)
            .Select(item => item is ApprovalItem approval ? approval.Approval.ID : ((AskItem)item).Ask.ID)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var row in _drawn.OfType<Border>())
        {
            var id = row.Tag switch
            {
                ApprovalItem approval => approval.Approval.ID,
                AskItem ask => ask.Ask.ID,
                _ => null,
            };
            if (id is null || before.Contains(id))
            {
                continue;
            }

            if (row.RenderTransform is not TranslateTransform)
            {
                row.RenderTransform = new TranslateTransform();
            }

            var animation = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(260),
                Easing = new SplineEasing(0.16, 1, 0.3, 1),
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0),
                        Setters = { new Setter(OpacityProperty, 0d), new Setter(TranslateTransform.XProperty, 14d) },
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1),
                        Setters = { new Setter(OpacityProperty, 1d), new Setter(TranslateTransform.XProperty, 0d) },
                    },
                },
            };
            _ = PlayPromptMotion(row, animation);
        }
    }

    /// <summary>Runs a prompt entrance until it settles or its row leaves the transcript.</summary>
    /// <param name="row">The prompt row.</param>
    /// <param name="animation">The entrance animation.</param>
    /// <returns>The completed entrance.</returns>
    private async Task PlayPromptMotion(Control row, Animation animation)
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
