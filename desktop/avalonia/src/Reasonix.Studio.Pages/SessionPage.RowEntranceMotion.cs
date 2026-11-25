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

namespace Reasonix.Studio.Pages;

public sealed partial class SessionPage
{
    /// <summary>Plays the reference card entrance for a newly arrived user message or answer.</summary>
    /// <param name="previous">The state before the row arrived.</param>
    private void AnimateTextRowEntrances(SessionState previous)
    {
        if (TopLevel.GetTopLevel(this) is not Window window || !window.Classes.Contains("motion"))
        {
            return;
        }

        var before = previous.Items.Where(item => item is UserItem or SayItem)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var row in Drawn.OfType<Border>())
        {
            var id = row.Tag switch
            {
                UserItem user => user.Id,
                SayItem say => say.Id,
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
            _ = PlayPromptMotion(row, animation);
        }
    }
}
