using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Pages;

public sealed partial class SessionPage
{
    /// <summary>Plays activity entrances caused by newly arrived rows in the viewport.</summary>
    /// <param name="previous">The state before those rows arrived.</param>
    private void AnimateActivityGroupEntrances(SessionState previous)
    {
        if (TopLevel.GetTopLevel(this) is not Window window || !window.Classes.Contains("motion"))
        {
            return;
        }

        var groups = _rows.GetVisualDescendants().OfType<ActivityDisclosure>()
            .Where(group => group.IsEffectivelyVisible && group.Tag is IReadOnlyList<TranscriptItem>);
        var known = previous.Items.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var rows = (IReadOnlyList<TranscriptItem>)group.Tag!;
            if (known.Contains(rows[0].Id))
            {
                continue;
            }

            group.RenderTransform = new TranslateTransform();
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
            _ = PlayPromptMotion(group, animation);
        }
    }
}
