using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using System;

namespace Reasonix.Studio.Shell.Workspace;

public sealed partial class WorkspaceMoveDialog
{
    /// <summary>Plays the reference short card entrance when the move dialog opens.</summary>
    private void EnterDialogCard()
    {
        if (TopLevel.GetTopLevel(this) is not Window window || !window.Classes.Contains("motion"))
        {
            return;
        }

        if (DialogCard.RenderTransform is not TranslateTransform)
        {
            DialogCard.RenderTransform = new TranslateTransform();
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
        _ = animation.RunAsync(DialogCard);
    }
}
