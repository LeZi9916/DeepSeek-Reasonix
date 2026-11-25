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
    /// <summary>Plays the reference short card entrance when the settings search results open.</summary>
    private void EnterSearchResults()
    {
        if (!Classes.Contains("motion"))
        {
            return;
        }

        if (SearchResultsPanel.RenderTransform is not TranslateTransform)
        {
            SearchResultsPanel.RenderTransform = new TranslateTransform();
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
        _ = animation.RunAsync(SearchResultsPanel);
    }
}
