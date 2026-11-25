using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using System;

namespace Reasonix.Studio.Shell.Workbench;

public sealed partial class WorkspaceFileRow
{
    /// <summary>Whether this row has already played its entrance.</summary>
    private bool _entered;

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        EnterRow();
    }

    /// <summary>Plays the reference card entrance once the file row is mounted.</summary>
    private void EnterRow()
    {
        if (_entered || TopLevel.GetTopLevel(this) is not Window window || !window.Classes.Contains("motion"))
        {
            return;
        }

        _entered = true;
        RenderTransform = new TranslateTransform();
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
        _ = animation.RunAsync(this);
    }
}
