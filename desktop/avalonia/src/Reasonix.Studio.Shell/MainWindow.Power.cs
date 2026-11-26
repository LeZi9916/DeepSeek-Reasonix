using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Host;
using System;
using System.Threading;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The inhibition for the current desktop process.</summary>
    private AppSleepLease? _powerLease;

    /// <summary>Starts the native shell's host-backed sleep preference.</summary>
    private void InitializePower()
    {
        if (!_connectOnOpen)
        {
            return;
        }

        Opened += (_, _) => MonitorPowerAsync();
        Closed += (_, _) => _powerLease?.Dispose();
    }

    /// <summary>Polls the owning host for active sessions, even while the window is hidden.</summary>
    private async void MonitorPowerAsync()
    {
        using var lease = new AppSleepLease(() => RequestPlatformInhibition(PlatformInhibitionType.AppSleep, "Reasonix Studio"));
        _powerLease = lease;
        var token = _closing.Token;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            do
            {
                if (_workspacePreferences.KeepAwakeWhileRunning && (_launchOwner ?? _engine) is { } owner)
                {
                    var running = 0;
                    var unknown = 1;
                    try
                    {
                        var state = await owner.Pages.TrayRunningAsync(token).ConfigureAwait(true);
                        running = (int?)state["panes"] ?? 0;
                        unknown = (int?)state["unknown"] ?? 1;
                    }
                    catch (Exception error) when (error is not OutOfMemoryException)
                    {
                    }

                    if (!token.IsCancellationRequested && _workspacePreferences.KeepAwakeWhileRunning)
                    {
                        lease.Observe(running, unknown);
                    }
                }
            }
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(true));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            _powerLease = null;
        }
    }

    /// <summary>Adds the local preference for preventing sleep during a turn.</summary>
    /// <param name="card">The settings window card.</param>
    private void AddPowerSetting(StackPanel card)
    {
        var option = new ToggleSwitch { IsChecked = _workspacePreferences.KeepAwakeWhileRunning };
        AutomationProperties.SetAutomationId(option, "action:chrome.keep-awake");
        AddRow(card, "settings.keep_the_computer_awake_while", option, MessageCatalog.Get("settings.while_a_session_is_running", Language));
        option.IsCheckedChanged += (_, _) =>
        {
            _workspacePreferences = _workspacePreferences with
            {
                KeepAwakeWhileRunning = option.IsChecked == true,
            };
            if (!_workspacePreferences.KeepAwakeWhileRunning)
            {
                _powerLease?.Dispose();
            }

            if (_connectOnOpen)
            {
                _workspacePreferences.Save();
            }
        };
    }
}
