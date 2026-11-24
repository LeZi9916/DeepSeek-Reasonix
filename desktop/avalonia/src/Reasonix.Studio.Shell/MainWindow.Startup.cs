using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Startup;
using System;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The system motion setting read for this window's startup.</summary>
    private Task<bool>? _startupMotion;

    /// <summary>The kernel that still expects this window to acknowledge its first run.</summary>
    private StudioPageClient? _unwelcomed;

    /// <summary>Starts the opening sequence with the operating system's motion preference.</summary>
    /// <returns>The startup preference read.</returns>
    private async Task StartBootAsync()
    {
        try
        {
            _startupMotion = ReducedEffects ? Task.FromResult(false) : _systemAllowsMotion(_closing.Token);
            var motion = await _startupMotion.ConfigureAwait(true);
            if (!_closing.IsCancellationRequested)
            {
                StartupIntro.Begin(motion, Language != InterfaceLanguage.Zh);
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
    }

    /// <summary>Lets the intro yield after the initial session and preferences are visible.</summary>
    /// <returns>The preparation of the first workspace view.</returns>
    private async Task FinishStartupAsync()
    {
        await LoadStartupPreferencesAsync().ConfigureAwait(true);
        if (!_closing.IsCancellationRequested)
        {
            StartupIntro.Progress(3);
            StartupIntro.Complete();
        }
    }

    /// <summary>Shows either required provider setup or the welcome owed by the configured workspace.</summary>
    /// <param name="pages">The startup session's kernel connection.</param>
    /// <returns>The initial setup decision.</returns>
    private async Task PrepareFirstRunAsync(StudioPageClient pages)
    {
        var seen = ReadWelcomeSeenAsync(pages);
        var setup = await pages.ProviderSetupAsync(_closing.Token).ConfigureAwait(true);
        var welcomed = await seen.ConfigureAwait(true);
        if (_closing.IsCancellationRequested)
        {
            return;
        }

        if (!welcomed)
        {
            _unwelcomed = pages;
        }

        if (setup is { Required: true })
        {
            ShowSetup(setup);
        }
        else if (!welcomed)
        {
            var motion = _startupMotion is { } startup
                ? await startup.ConfigureAwait(true)
                : Classes.Contains("motion");
            if (!_closing.IsCancellationRequested)
            {
                FirstRunCurtain.Begin(Language, motion && !ReducedEffects);
            }
        }
    }

    /// <summary>Reads the host-owned marker without making a refused read delay the workspace.</summary>
    /// <param name="pages">The startup session's kernel connection.</param>
    /// <returns>Whether the first-run animation can be omitted.</returns>
    private async Task<bool> ReadWelcomeSeenAsync(StudioPageClient pages)
    {
        try
        {
            return await pages.WelcomeSeenAsync(_closing.Token).ConfigureAwait(true);
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>Records a completed or explicitly dismissed first-run curtain.</summary>
    private void FinishFirstRunCurtain()
    {
        if (Composer.IsEffectivelyVisible)
        {
            Composer.Focus();
        }

        _ = MarkFirstRunWelcomedAsync();
    }

    /// <summary>Acknowledges the first-run experience on the connection that requested it.</summary>
    /// <returns>The optional kernel acknowledgement.</returns>
    private async Task MarkFirstRunWelcomedAsync()
    {
        if (_unwelcomed is not { } pages || _closing.IsCancellationRequested)
        {
            return;
        }

        _unwelcomed = null;
        try
        {
            await pages.MarkWelcomedAsync(_closing.Token).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // The old frontend lets the next startup read the marker again after a failed write.
        }
    }

    /// <summary>Fades in the first page behind the retiring intro.</summary>
    private async void EnterStartupPage()
    {
        if (!StartupIntro.AllowsMotion || _closing.IsCancellationRequested)
        {
            return;
        }

        var closed = _motionLifetime.Token;
        var entry = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(1200),
            Easing = new SplineEasing(0.4, 0, 0.2, 1),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0d), new Setter(TranslateTransform.YProperty, 6d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1d), new Setter(TranslateTransform.YProperty, 0d) } },
            },
        };
        var sweep = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(1150),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(BootChromeSweep.PhaseProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(BootChromeSweep.PhaseProperty, 1d) } },
            },
        };
        try
        {
            await Task.WhenAll(entry.RunAsync(ScaledWindow, closed), sweep.RunAsync(StartupChromeSweep, closed)).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (closed.IsCancellationRequested)
        {
        }
    }
}
