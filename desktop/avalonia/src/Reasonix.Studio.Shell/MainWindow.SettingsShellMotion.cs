using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The window settings sheet transition currently owning the layers.</summary>
    private CancellationTokenSource? _settingsShellMotion;

    /// <summary>Shows the settings sheet over the retained workspace during its entrance.</summary>
    private async void EnterSettingsShell()
    {
        if (await AnimateSettingsShellAsync(opening: true).ConfigureAwait(true) && SettingsPanel.IsVisible && !SetupPanel.IsVisible)
        {
            LayoutRoot.IsVisible = false;
        }
    }

    /// <summary>Moves the sheet independently from its backdrop.</summary>
    /// <param name="opening">Whether the settings sheet is being shown.</param>
    /// <returns>Whether the requested settings transition is still current.</returns>
    private async Task<bool> AnimateSettingsShellAsync(bool opening)
    {
        var running = _settingsShellMotion;
        var sheetOpacity = running is null && opening ? 0 : SettingsSheet.Opacity;
        var veilOpacity = running is null && opening ? 0 : SettingsVeil.Opacity;
        var titleOpacity = running is null && opening ? 0 : SettingsTitleBar.Opacity;
        var transform = (TranslateTransform)SettingsSheet.RenderTransform!;
        var x = running is null && opening ? 14 : transform.X;
        var y = transform.Y;
        running?.Cancel();
        if (!Classes.Contains("motion") || _closing.IsCancellationRequested)
        {
            return !_closing.IsCancellationRequested;
        }

        if (!opening)
        {
            LayoutRoot.IsVisible = true;
        }

        using var motion = CancellationTokenSource.CreateLinkedTokenSource(_motionLifetime.Token);
        _settingsShellMotion = motion;
        var fadeDuration = opening ? 180 : 120;
        Easing fade = opening ? new SplineEasing(0.16, 1, 0.3, 1) : new SplineEasing(0.55, 0, 1, 0.45);
        var veil = Fade(veilOpacity, opening ? 1 : 0, fadeDuration, fade);
        var title = Fade(titleOpacity, opening ? 1 : 0, fadeDuration, fade);
        var sheet = Fade(sheetOpacity, opening ? 1 : 0, opening ? 260 : 120, opening ? new Reasonix.Studio.Controls.SpringEasing() : fade);
        sheet.Children[0].Setters.Add(new Setter(TranslateTransform.XProperty, x));
        sheet.Children[1].Setters.Add(new Setter(TranslateTransform.XProperty, 0d));
        sheet.Children[0].Setters.Add(new Setter(TranslateTransform.YProperty, y));
        sheet.Children[1].Setters.Add(new Setter(TranslateTransform.YProperty, opening ? 0d : 4d));
        try
        {
            await Task.WhenAll(
                veil.RunAsync(SettingsVeil, motion.Token),
                title.RunAsync(SettingsTitleBar, motion.Token),
                sheet.RunAsync(SettingsSheet, motion.Token)).ConfigureAwait(true);
            return _settingsShellMotion == motion && !_closing.IsCancellationRequested;
        }
        catch (OperationCanceledException) when (motion.IsCancellationRequested)
        {
            return _settingsShellMotion == motion && !Classes.Contains("motion") && !_closing.IsCancellationRequested;
        }
        finally
        {
            if (_settingsShellMotion == motion)
            {
                _settingsShellMotion = null;
            }
        }

        static Animation Fade(double from, double to, int duration, Easing easing) => new()
        {
            Duration = TimeSpan.FromMilliseconds(duration),
            Easing = easing,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, from) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, to) } },
            },
        };
    }
}
