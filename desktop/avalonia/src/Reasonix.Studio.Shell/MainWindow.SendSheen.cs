using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The current send-sheen pass, replaced on every re-entry.</summary>
    private CancellationTokenSource? _sendSheenMotion;

    /// <summary>Plays the reference send sheen once across the button.</summary>
    internal void PlaySendSheen()
    {
        if (!Send.IsEnabled || !Classes.Contains("motion") || _closing.IsCancellationRequested
            || SendSheen.RenderTransform is not TranslateTransform transform)
        {
            return;
        }

        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(700),
            FillMode = FillMode.Forward,
            Easing = new SplineEasing(0.16, 1, 0.3, 1),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters = { new Setter(OpacityProperty, 1d), new Setter(TranslateTransform.XProperty, 96d) },
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters = { new Setter(OpacityProperty, 1d), new Setter(TranslateTransform.XProperty, -96d) },
                },
            },
        };
        BeginSendSheen(animation, transform, opacity: 1, offset: 96);
    }

    /// <summary>Retires a visible sheen at its current location on pointer exit.</summary>
    private void ExitSendSheen()
    {
        if (_sendSheenMotion is null || _closing.IsCancellationRequested || SendSheen.RenderTransform is not TranslateTransform transform)
        {
            return;
        }

        if (!Classes.Contains("motion"))
        {
            _sendSheenMotion.Cancel();
            return;
        }

        var opacity = SendSheen.Opacity;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(120),
            Easing = new SplineEasing(0.16, 1, 0.3, 1),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, opacity) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 0d) } },
            },
        };
        BeginSendSheen(animation, transform, opacity, transform.X);
    }

    /// <summary>Replaces the running sheen without allowing its cleanup to overwrite the next pose.</summary>
    /// <param name="animation">The requested animation.</param><param name="transform">The sheen translation.</param>
    /// <param name="opacity">Initial opacity.</param><param name="offset">Initial offset.</param>
    private void BeginSendSheen(Animation animation, TranslateTransform transform, double opacity, double offset)
    {
        var previous = _sendSheenMotion;
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_motionLifetime.Token);
        _sendSheenMotion = lifetime;
        previous?.Cancel();
        SendSheen.Opacity = opacity;
        transform.X = offset;
        _ = PlayAsync(animation, transform, lifetime);
    }

    /// <summary>Runs the sheen and restores its resting pose.</summary>
    /// <param name="animation">The sheen animation.</param>
    /// <param name="transform">The sheen translation.</param>
    /// <param name="lifetime">The current pass.</param>
    /// <returns>The completed pass.</returns>
    private async Task PlayAsync(Animation animation, TranslateTransform transform, CancellationTokenSource lifetime)
    {
        try
        {
            await animation.RunAsync(SendSheen, lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_sendSheenMotion, lifetime))
            {
                SendSheen.Opacity = 0;
                transform.X = 96;
                _sendSheenMotion = null;
            }

            lifetime.Dispose();
        }
    }

    /// <summary>Wires the reference hover sheen on the send control.</summary>
    private void InitializeSendSheen()
    {
        Send.PointerEntered += (_, _) => PlaySendSheen();
        Send.PointerExited += (_, _) => ExitSendSheen();
    }
}
