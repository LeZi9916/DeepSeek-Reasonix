using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Reasonix.Studio.Shell.Workspace;
using System;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The reference polls canonical presence every three seconds.</summary>
    private readonly DispatcherTimer _deviceRefresh = new() { Interval = TimeSpan.FromSeconds(3) };

    /// <summary>The current device read, shared by the badge and open card.</summary>
    private Task? _deviceReading;

    /// <summary>The explicitly opened device access surface.</summary>
    private DeviceAccessCard? _deviceCard;

    /// <summary>The single anchored device popup.</summary>
    private Flyout? _devicePopup;

    /// <summary>Reads presence only while the desktop window owns its launched hub.</summary>
    private void InitializeDeviceAccess()
    {
        _deviceRefresh.Tick += async (_, _) => await ReadDevicePresenceAsync().ConfigureAwait(true);
        Opened += async (_, _) =>
        {
            if (_connectOnOpen)
            {
                _deviceRefresh.Start();
                await ReadDevicePresenceAsync().ConfigureAwait(true);
            }
        };
        Closed += (_, _) => _deviceRefresh.Stop();
    }

    /// <summary>Refreshes canonical presence without requesting or exposing a pairing credential.</summary>
    /// <returns>The latest shared read.</returns>
    private async Task ReadDevicePresenceAsync()
    {
        if (_deviceReading is { IsCompleted: false } || (_launchOwner ?? _engine) is not { } owner)
        {
            return;
        }

        _deviceReading = Read();
        await _deviceReading.ConfigureAwait(true);
        async Task Read()
        {
            try
            {
                var status = await owner.Pages.DeviceAccessAsync(_closing.Token).ConfigureAwait(true);
                WorkspaceStatus.IsVisible = status is not null;
                if (status is null)
                {
                    return;
                }

                WorkspaceStatus.ShowDevices(status, Language, _devicePopup?.IsOpen == true);
                if (_devicePopup?.IsOpen == true)
                {
                    _deviceCard?.ShowStatus(status);
                }
            }
            catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
            catch (Exception) { }
        }
    }

    /// <summary>Opens device access against the window's owning hub.</summary>
    private async void OpenDeviceAccess()
    {
        if (_devicePopup?.IsOpen == true)
        {
            _devicePopup.Hide();
            return;
        }

        if ((_launchOwner ?? _engine) is not { } engine)
        {
            return;
        }

        var card = new DeviceAccessCard();
        var flyout = new Flyout { Content = card, Placement = PlacementMode.BottomEdgeAlignedRight };
        _devicePopup = flyout;
        _deviceCard = card;
        card.StatusChanged += status => WorkspaceStatus.ShowDevices(status, Language, _devicePopup?.IsOpen == true);
        WorkspaceStatus.HidePresence();
        flyout.ShowAt(WorkspaceStatus);
        await card.OpenAsync(engine.Pages, Language).ConfigureAwait(true);
    }
}
