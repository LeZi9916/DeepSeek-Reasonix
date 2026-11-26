using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The in-flight exit of the active onboarding card.</summary>
    private CancellationTokenSource? _setupDeparture;

    /// <summary>Lets the onboarding card leave before taking down the setup surface.</summary>
    /// <returns>Whether to show the conversation after this request.</returns>
    private async Task<bool> PlaySetupDepartureAsync()
    {
        if (_setupDeparture is not null)
        {
            return false;
        }

        if (!Classes.Contains("motion"))
        {
            return !_motionLifetime.IsCancellationRequested;
        }

        Control card = WelcomeCard.IsVisible ? WelcomeCard : ProjectStep.IsVisible ? ProjectStep : SetupShell;
        CancelWelcomeEntrance();
        using var exit = CancellationTokenSource.CreateLinkedTokenSource(_motionLifetime.Token);
        _setupDeparture = exit;
        SetupPanel.IsHitTestVisible = false;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(240),
            FillMode = FillMode.Forward,
            Easing = new SplineEasing(0.42, 0, 1, 1),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 1d), new Setter(ScaleTransform.ScaleXProperty, 1d), new Setter(ScaleTransform.ScaleYProperty, 1d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 0d), new Setter(ScaleTransform.ScaleXProperty, 0.94d), new Setter(ScaleTransform.ScaleYProperty, 0.94d) } },
            },
        };
        try
        {
            await animation.RunAsync(card, exit.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (exit.IsCancellationRequested)
        {
        }
        finally
        {
            _setupDeparture = null;
            SetupPanel.IsHitTestVisible = true;
        }

        return !_motionLifetime.IsCancellationRequested;
    }

    /// <summary>Restores cards for the next visit or a superseded departure.</summary>
    private void ResetSetupDeparturePose()
    {
        foreach (var card in new Control[] { WelcomeCard, ProjectStep, SetupShell })
        {
            card.Opacity = 1;
            var scale = card.RenderTransform as ScaleTransform
                ?? (card.RenderTransform as TransformGroup)?.Children.OfType<ScaleTransform>().FirstOrDefault();
            if (scale is not null)
            {
                scale.ScaleX = scale.ScaleY = 1;
            }
        }
    }
}
