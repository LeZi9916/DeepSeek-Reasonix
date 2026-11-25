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
        if (!Send.IsEnabled || !Classes.Contains("motion") || SendSheen.RenderTransform is not TranslateTransform transform)
        {
            return;
        }

        _sendSheenMotion?.Cancel();
        var lifetime = new CancellationTokenSource();
        _sendSheenMotion = lifetime;
        SendSheen.Opacity = 1;
        transform.X = 96;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(700),
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
    private void InitializeSendSheen() => Send.PointerEntered += (_, _) => PlaySendSheen();
}
