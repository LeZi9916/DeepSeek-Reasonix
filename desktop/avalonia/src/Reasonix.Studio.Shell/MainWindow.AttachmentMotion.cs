using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Collections.Generic;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The attachment chips whose entrance has already played.</summary>
    private readonly HashSet<AttachmentChip> _renderedAttachments = new(ReferenceEqualityComparer.Instance);

    /// <summary>Plays the reference staggered card entrance for a new attachment chip.</summary>
    /// <param name="chip">The new attachment chip.</param>
    /// <param name="index">Its position in the strip, which sets the reference stagger.</param>
    private void EnterAttachment(Border chip, int index)
    {
        if (TopLevel.GetTopLevel(this) is not Window window || !window.Classes.Contains("motion"))
        {
            return;
        }

        chip.RenderTransform = new TranslateTransform();
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(120),
            Delay = TimeSpan.FromMilliseconds(45 * index),
            FillMode = FillMode.Both,
            Easing = new SplineEasing(0.25, 0.1, 0.25, 1),
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
        _ = animation.RunAsync(chip);
    }
}
