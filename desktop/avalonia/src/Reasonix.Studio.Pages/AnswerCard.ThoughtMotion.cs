using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Pages;

internal sealed partial class AnswerCard
{
    /// <summary>The live-to-settled label transition, when this card witnessed it.</summary>
    private CancellationTokenSource? _thoughtSwap;

    /// <summary>Builds a reasoning label transition using the reference card timing.</summary>
    /// <param name="from">Starting opacity.</param>
    /// <param name="to">Final opacity.</param>
    /// <param name="slide">Whether the label slides down as it leaves.</param>
    /// <returns>The transition for one of the labels.</returns>
    private static Animation ThoughtFade(double from, double to, bool slide = false)
    {
        var first = new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, from) } };
        var last = new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, to) } };
        if (slide)
        {
            first.Setters.Add(new Setter(TranslateTransform.YProperty, 0d));
            last.Setters.Add(new Setter(TranslateTransform.YProperty, 4d));
        }

        return new Animation
        {
            Duration = TimeSpan.FromMilliseconds(180),
            Easing = new SplineEasing(0.16, 1, 0.3, 1),
            Children = { first, last },
        };
    }

    /// <summary>Fades the new label in as the live label exits.</summary>
    /// <param name="previous">The outgoing live label.</param>
    private async void SwapThoughtLabel(string previous)
    {
        if (TopLevel.GetTopLevel(this) is not Window window || !window.Classes.Contains("motion"))
        {
            return;
        }

        _thoughtSwap?.Cancel();
        using var swap = new CancellationTokenSource();
        _thoughtSwap = swap;
        var outgoing = this.FindControl<TextBlock>("ThoughtOut")!;
        outgoing.Text = previous;
        outgoing.IsVisible = true;
        NotifyCollectionChangedEventHandler motionChanged = (_, _) =>
        {
            if (!window.Classes.Contains("motion"))
            {
                swap.Cancel();
            }
        };
        window.Classes.CollectionChanged += motionChanged;
        try
        {
            await Task.WhenAll(
                ThoughtFade(0, 1).RunAsync(_thoughtLabel, swap.Token),
                ThoughtFade(1, 0, slide: true).RunAsync(outgoing, swap.Token)).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (swap.IsCancellationRequested)
        {
        }
        finally
        {
            window.Classes.CollectionChanged -= motionChanged;
            if (_thoughtSwap == swap)
            {
                _thoughtSwap = null;
                outgoing.IsVisible = false;
                outgoing.Text = null;
            }
        }
    }
}
