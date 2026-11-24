using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Reasonix.Studio.Shell.Settings;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The current welcome page's entrance.</summary>
    private CancellationTokenSource? _welcomeMotion;

    /// <summary>Creates the timed mark wipe or the following text movement.</summary>
    /// <param name="delayMs">Delay from the welcome page opening.</param>
    /// <param name="wipe">Whether to also reveal the wordmark width.</param>
    /// <returns>The reveal from the Electron onboarding.</returns>
    private static Animation WelcomeReveal(int delayMs, bool wipe = false)
    {
        var start = new KeyFrame
        {
            Cue = new Cue(0),
            Setters = { new Setter(OpacityProperty, 0d), new Setter(TranslateTransform.YProperty, 10d) },
        };
        var finish = new KeyFrame
        {
            Cue = new Cue(1),
            Setters = { new Setter(OpacityProperty, 1d), new Setter(TranslateTransform.YProperty, 0d) },
        };
        if (wipe)
        {
            start.Setters.Add(new Setter(OnboardingReveal.FractionProperty, 0d));
            finish.Setters.Add(new Setter(OnboardingReveal.FractionProperty, 1d));
        }

        return new Animation
        {
            Duration = TimeSpan.FromMilliseconds(wipe ? 700 : 420),
            Delay = TimeSpan.FromMilliseconds(delayMs),
            FillMode = FillMode.Backward,
            Easing = new SplineEasing(0.16, 1, 0.3, 1),
            Children = { start, finish },
        };
    }

    /// <summary>Plays the welcome reveal for a newly entered welcome stage.</summary>
    private async void EnterWelcome()
    {
        if (_welcomeMotion is not null || !SetupPanel.IsVisible || !WelcomeCard.IsVisible || !Classes.Contains("motion") || _closing.IsCancellationRequested)
        {
            return;
        }

        using var reveal = CancellationTokenSource.CreateLinkedTokenSource(_motionLifetime.Token);
        _welcomeMotion = reveal;
        try
        {
            await Task.WhenAll(
                WelcomeReveal(0, wipe: true).RunAsync(WelcomeMark, reveal.Token),
                WelcomeReveal(520).RunAsync(WelcomeTitle, reveal.Token),
                WelcomeReveal(600).RunAsync(WelcomeDescription, reveal.Token),
                WelcomeReveal(680).RunAsync(WelcomeActions, reveal.Token),
                WelcomeReveal(760).RunAsync(SetupTime, reveal.Token)).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (reveal.IsCancellationRequested)
        {
        }
        finally
        {
            if (_welcomeMotion == reveal)
            {
                _welcomeMotion = null;
            }
        }
    }

    /// <summary>Stops the reveal when the stage is left or motion is reduced.</summary>
    private void CancelWelcomeEntrance()
    {
        var reveal = _welcomeMotion;
        _welcomeMotion = null;
        reveal?.Cancel();
    }
}
