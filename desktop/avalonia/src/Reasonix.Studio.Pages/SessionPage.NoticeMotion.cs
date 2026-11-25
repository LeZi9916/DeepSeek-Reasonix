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
    /// <summary>Plays the reference short card entrance for a newly arrived kernel notice.</summary>
    /// <param name="previous">The state before the notice arrived.</param>
    private void AnimateNoticeEntrances(SessionState previous)
    {
        if (TopLevel.GetTopLevel(this) is not Window window || !window.Classes.Contains("motion"))
        {
            return;
        }

        var before = previous.Items.OfType<NoticeItem>().Select(notice => notice.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var row in _drawn.OfType<Border>())
        {
            if (row.Tag is not NoticeItem notice || before.Contains(notice.Id))
            {
                continue;
            }

            if (row.RenderTransform is not TranslateTransform)
            {
                row.RenderTransform = new TranslateTransform();
            }

            var animation = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(120),
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
