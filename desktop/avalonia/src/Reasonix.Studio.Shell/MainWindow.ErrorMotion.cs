using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using System;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Shows a refusal in the active surface, and in the window notice when that surface is hidden.</summary>
    /// <param name="message">The sentence the reader sees.</param>
    internal void ShowErrorBar(string message)
    {
        var wasVisible = ErrorBar.IsVisible;
        ErrorText.Text = message;
        ErrorBar.IsVisible = true;
        if (!wasVisible)
        {
            EnterError(ErrorBar);
        }

        if (!ErrorBar.IsEffectivelyVisible)
        {
            GlobalErrorText.Text = message;
            GlobalError.IsVisible = true;
            if (!wasVisible)
            {
                EnterError(GlobalError);
            }
        }
    }

    /// <summary>Plays the reference card entrance on a newly shown error bar.</summary>
    /// <param name="bar">The bar being shown.</param>
    private void EnterError(Border bar)
    {
        if (!Classes.Contains("motion"))
        {
            return;
        }

        if (bar.RenderTransform is not TranslateTransform)
        {
            bar.RenderTransform = new TranslateTransform();
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
        _ = animation.RunAsync(bar);
    }
}
