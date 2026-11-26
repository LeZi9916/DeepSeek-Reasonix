using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Specialized;

namespace Reasonix.Studio.Pages;

internal sealed partial class ActivityDisclosure
{
    /// <summary>The number of calls in the acknowledged group.</summary>
    private int _callCount;

    /// <summary>Displays stationary details next to the numeric label.</summary>
    /// <param name="count">The group's acknowledged call count.</param>
    /// <param name="details">Failures and elapsed time, when present.</param>
    internal void SetCountDetails(int count, string details)
    {
        _callCount = count;
        var suffix = this.FindControl<TextBlock>("SummaryDetails")!;
        suffix.Text = details;
        suffix.IsVisible = details.Length > 0;
        AutomationProperties.SetName(this.FindControl<Button>("Toggle")!, this.FindControl<TextBlock>("Summary")!.Text + details);
    }

    /// <summary>Updates a group whose mounted header survived a live frame.</summary>
    /// <param name="count">Canonical call count.</param><param name="label">The new count text.</param>
    /// <param name="details">Supplementary status text.</param><param name="animate">Whether a new call can animate.</param>
    internal void UpdateCount(int count, string label, string details, bool animate)
    {
        var previous = _callCount;
        var summary = this.FindControl<TextBlock>("Summary")!;
        summary.Text = label;
        SetCountDetails(count, details);
        if (animate && count != previous && TopLevel.GetTopLevel(this) is Window window && window.Classes.Contains("motion"))
        {
            Roll((TranslateTransform)summary.RenderTransform!, summary.LineHeight, window);
        }
    }

    /// <summary>Plays a count change, or retains a running transform when the same count is redrawn.</summary>
    /// <param name="previous">The prior visible heading of this group.</param>
    internal void ContinueCountMotion(ActivityDisclosure previous)
    {
        var summary = this.FindControl<TextBlock>("Summary")!;
        if (_callCount == previous._callCount)
        {
            summary.RenderTransform = previous.FindControl<TextBlock>("Summary")!.RenderTransform;
            return;
        }

        var transform = (TranslateTransform)summary.RenderTransform!;
        Roll(transform, summary.LineHeight, (Window)TopLevel.GetTopLevel(this)!);
    }

    /// <summary>Animates the label for the reference duration and settles on reduced motion.</summary>
    /// <param name="transform">The transform shared across consecutive presentations of the count.</param>
    /// <param name="height">One line of count text.</param>
    /// <param name="window">The owning window and its effective motion preference.</param>
    private static void Roll(TranslateTransform transform, double height, Window window)
    {
        transform.Y = height;
        transform.Transitions =
        [
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = TimeSpan.FromMilliseconds(120),
                Easing = new SplineEasing(0.16, 1, 0.3, 1),
            },
        ];
        window.Classes.CollectionChanged += MotionChanged;
        transform.PropertyChanged += Finish;
        transform.Y = 0;

        void Finish(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == TranslateTransform.YProperty && transform.Y == 0)
            {
                Disconnect();
            }
        }

        void MotionChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (!window.Classes.Contains("motion"))
            {
                Disconnect();
                transform.Transitions = null;
            }
        }

        void Disconnect()
        {
            transform.PropertyChanged -= Finish;
            window.Classes.CollectionChanged -= MotionChanged;
        }
    }
}
