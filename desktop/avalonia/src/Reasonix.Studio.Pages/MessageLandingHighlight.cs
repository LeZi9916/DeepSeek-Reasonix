using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using System;
using System.Threading;

namespace Reasonix.Studio.Pages;

/// <summary>Paints the landing ring in viewport coordinates so the bubble does not clip it.</summary>
internal sealed class MessageLandingHighlight : Control
{
    /// <summary>The interpolated spread and coverage of the transcript landing shadow.</summary>
    private static readonly StyledProperty<double> s_amount = AvaloniaProperty.Register<MessageLandingHighlight, double>("Amount");

    /// <summary>The current bubble anchor and its in-flight highlight.</summary>
    private (Border Target, CancellationTokenSource Motion)? _active;

    /// <summary>Initializes static members of the <see cref="MessageLandingHighlight"/> class.</summary>
    static MessageLandingHighlight() => AffectsRender<MessageLandingHighlight>(s_amount);

    /// <summary>Initializes a new instance of the <see cref="MessageLandingHighlight"/> class.</summary>
    public MessageLandingHighlight()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        LayoutUpdated += (_, _) => Follow();
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        var amount = GetValue(s_amount);
        if (amount > 0 && _active is { Target: var target }
            && target.TranslatePoint(default, this) is Point origin
            && this.TryFindResource("Net", ActualThemeVariant, out var value) && value is Color net)
        {
            var spread = 3 * amount;
            var path = new Rect(origin, target.Bounds.Size).Inflate(spread / 2);
            context.DrawRectangle(null, new Pen(new SolidColorBrush(net, 0.45 * amount), spread), new RoundedRect(path, 15 + (spread / 2)));
        }
    }

    /// <summary>Follows the active bubble as the reader scrolls or the transcript changes size.</summary>
    internal void Follow()
    {
        if (_active is not null)
        {
            InvalidateVisual();
        }
    }

    /// <summary>Stops drawing a landing for the previously mounted conversation.</summary>
    internal void Clear()
    {
        var previous = _active;
        _active = null;
        previous?.Motion.Cancel();
        InvalidateVisual();
    }

    /// <summary>Highlights the mounted bubble targeted by the reader.</summary>
    /// <param name="target">The message bubble that received the jump.</param>
    internal async void Play(Border target)
    {
        Clear();
        using var motion = new CancellationTokenSource();
        _active = (target, motion);
        target.DetachedFromVisualTree += Cancel;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(900),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(s_amount, 0d) } },
                new KeyFrame { Cue = new Cue(0.3), KeySpline = new KeySpline(0.25, 0.1, 0.25, 1), Setters = { new Setter(s_amount, 1d) } },
                new KeyFrame { Cue = new Cue(1), KeySpline = new KeySpline(0.25, 0.1, 0.25, 1), Setters = { new Setter(s_amount, 0d) } },
            },
        };

        try
        {
            await animation.RunAsync(this, motion.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (motion.IsCancellationRequested)
        {
        }
        finally
        {
            target.DetachedFromVisualTree -= Cancel;
            if (_active?.Motion == motion)
            {
                _active = null;
                InvalidateVisual();
            }
        }

        void Cancel(object? sender, VisualTreeAttachmentEventArgs args) => motion.Cancel();
    }
}
