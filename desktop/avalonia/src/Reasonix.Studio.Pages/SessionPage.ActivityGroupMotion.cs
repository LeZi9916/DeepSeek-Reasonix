using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Collections.Generic;

namespace Reasonix.Studio.Pages;

public sealed partial class SessionPage
{
    /// <summary>The activity groups that have already entered.</summary>
    private readonly HashSet<string> _activityGroupsEntered = new(StringComparer.Ordinal);

    /// <summary>Plays the reference card entrance when an activity group first mounts.</summary>
    /// <param name="group">The new activity group.</param>
    private void EnterActivityGroup(ActivityDisclosure group)
    {
        void Attached(object? sender, VisualTreeAttachmentEventArgs args)
        {
            group.AttachedToVisualTree -= Attached;
            if (TopLevel.GetTopLevel(group) is not Window window || !window.Classes.Contains("motion"))
            {
                return;
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
            _ = animation.RunAsync(group);
        }

        group.AttachedToVisualTree += Attached;
    }
}
