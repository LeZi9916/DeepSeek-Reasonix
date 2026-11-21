using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Reads the system motion setting independently of the application effects preference.</summary>
    private readonly Func<CancellationToken, Task<bool>> _systemAllowsMotion;

    /// <summary>Orders asynchronous desktop preference reads across activations.</summary>
    private long _motionRead;

    /// <summary>The transition triggered by the latest user navigation.</summary>
    private CancellationTokenSource? _viewMotion;

    /// <summary>Gets a value indicating whether the user selected reduced visual effects.</summary>
    internal bool ReducedEffects
    {
        get; private set;
    }

    /// <summary>Reads the Windows client-area animation preference.</summary>
    /// <param name="action">SPI_GETCLIENTAREAANIMATION.</param>
    /// <param name="parameter">Unused for this query.</param>
    /// <param name="enabled">Receives the current setting.</param>
    /// <param name="flags">Unused for this query.</param>
    /// <returns>Nonzero when the preference was available.</returns>
    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    private static partial int SystemParametersInfo(uint action, uint parameter, out int enabled, uint flags);

    /// <summary>Reads the system preference without blocking the renderer.</summary>
    /// <param name="token">Cancels when the window closes.</param>
    /// <returns>Whether the desktop allows motion.</returns>
    private static async Task<bool> SystemAllowsMotionAsync(CancellationToken token)
    {
        if (OperatingSystem.IsWindows())
        {
            return SystemParametersInfo(0x1042, 0, out var enabled, 0) != 0 && enabled != 0;
        }

        var mac = OperatingSystem.IsMacOS();
        var start = mac
            ? new ProcessStartInfo("/usr/bin/defaults", "read com.apple.universalaccess reduceMotion")
            : new ProcessStartInfo("gsettings", "get org.gnome.desktop.interface enable-animations");
        start.UseShellExecute = false;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return false;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                var output = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                return mac ? output.Trim() != "1" : process.ExitCode == 0 && output.Trim() == "true";
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }

                return false;
            }
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Animates a user-requested view transition. State refreshes never call this path.</summary>
    /// <param name="view">Newly opened view.</param>
    private async void EnterView(Control view)
    {
        _viewMotion?.Cancel();
        if (!Classes.Contains("motion") || _closing.IsCancellationRequested)
        {
            return;
        }

        using var change = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        _viewMotion = change;
        var fade = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(260),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1), KeySpline = new KeySpline(0.16, 1, 0.3, 1), Setters = { new Setter(Visual.OpacityProperty, 1d) } },
            },
        };
        try
        {
            await fade.RunAsync(view, change.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (change.IsCancellationRequested)
        {
        }
        finally
        {
            if (_viewMotion == change)
            {
                _viewMotion = null;
            }
        }
    }

    /// <summary>Applies the effective preference without replaying mounted content.</summary>
    private async void ApplyMotion()
    {
        if (_closing.IsCancellationRequested)
        {
            return;
        }

        var current = ++_motionRead;
        Resources["ComposerShadow"] = ReducedEffects ? default(BoxShadows) : BoxShadows.Parse("0 3 12 0 #12191E19");
        var allowed = !ReducedEffects && await _systemAllowsMotion(_closing.Token).ConfigureAwait(true);
        if (current == _motionRead && !_closing.IsCancellationRequested)
        {
            Classes.Set("motion", allowed);
            if (!allowed)
            {
                _viewMotion?.Cancel();
            }
        }
    }
}
