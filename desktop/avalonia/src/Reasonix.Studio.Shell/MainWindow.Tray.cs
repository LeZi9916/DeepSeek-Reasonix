using Avalonia.Controls;
using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The icon actually available for reopening this window.</summary>
    private TrayIcon? _statusIcon;

    /// <summary>The menu's current kernel-backed close preference.</summary>
    private NativeMenuItem? _closeToTray;

    /// <summary>The open action, displayed in the kernel's interface language.</summary>
    private NativeMenuItem? _openFromTray;

    /// <summary>The session status as reported by the kernel.</summary>
    private NativeMenuItem? _trayLine;

    /// <summary>The action that exits even when close-to-tray is enabled.</summary>
    private NativeMenuItem? _quitFromTray;

    /// <summary>Last acknowledged close behavior.</summary>
    private bool _hideOnClose;

    /// <summary>Connects the platform tray menu to the window.</summary>
    /// <param name="icon">The native icon.</param>
    /// <param name="raise">Raises the existing window.</param>
    internal void AttachTray(TrayIcon icon, Action raise)
    {
        _statusIcon = icon;
        _openFromTray = new NativeMenuItem("Reasonix Studio");
        _openFromTray.Click += (_, _) => raise();
        _trayLine = new NativeMenuItem("—") { IsEnabled = false };
        _quitFromTray = new NativeMenuItem("Quit");
        _quitFromTray.Click += (_, _) =>
        {
            _hideOnClose = false;
            Close();
        };
        _closeToTray = new NativeMenuItem("Close to tray") { ToggleType = MenuItemToggleType.CheckBox };
        _closeToTray.Click += async (_, _) =>
        {
            if (_engine is null)
            {
                return;
            }

            _closeToTray.IsEnabled = false;
            try
            {
                var saved = await _engine.Pages.SetCloseToTrayAsync(!_hideOnClose, _closing.Token).ConfigureAwait(true);
                ApplyTrayPrefs(saved);
            }
            catch (OperationCanceledException) when (_closing.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                _closeToTray.IsChecked = _hideOnClose;
                ShowRefusal(error);
            }
            finally
            {
                _closeToTray.IsEnabled = true;
            }
        };
        icon.Menu = new NativeMenu { _openFromTray, new NativeMenuItemSeparator(), _trayLine, _closeToTray, new NativeMenuItemSeparator(), _quitFromTray };
        Closing += (_, e) =>
        {
            if (e.CloseReason == WindowCloseReason.WindowClosing && _hideOnClose && _statusIcon?.IsVisible == true)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }

    /// <summary>Removes an unavailable tray as a destination for the window.</summary>
    internal void DetachTray()
    {
        _statusIcon = null;
        _hideOnClose = false;
    }

    /// <summary>Adopts preferences returned by the tray endpoint.</summary>
    /// <param name="prefs">The latest preferences.</param>
    private void ApplyTrayPrefs(JsonObject prefs)
    {
        _hideOnClose = (bool?)prefs["closeToTray"] == true;
        _closeToTray!.IsChecked = _hideOnClose;
        _statusIcon!.IsVisible = (bool?)prefs["icon"] == true;
    }

    /// <summary>Keeps the same native menu installed while refreshing its labels and state.</summary>
    /// <returns>The tray polling lifetime.</returns>
    private async Task RefreshTrayAsync()
    {
        if (_engine is null || _statusIcon is null)
        {
            return;
        }

        var token = _closing.Token;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            do
            {
                try
                {
                    var prefs = await _engine.Pages.TrayPrefsAsync(token).ConfigureAwait(true);
                    if (_closeToTray!.IsEnabled)
                    {
                        ApplyTrayPrefs(prefs);
                    }

                    var state = await _engine.Pages.TrayStateAsync(token).ConfigureAwait(true);
                    var labels = state["labels"];
                    var line = (string?)state["line"];
                    _openFromTray!.Header = (string?)labels?["open"] ?? "Reasonix Studio";
                    _closeToTray.Header = (string?)labels?["closeToTray"] ?? _closeToTray.Header;
                    _quitFromTray!.Header = (string?)labels?["quit"] ?? _quitFromTray.Header;
                    _trayLine!.Header = string.IsNullOrEmpty(line) ? "—" : line;
                    _statusIcon.ToolTipText = string.IsNullOrEmpty(line) ? "Reasonix Studio" : "Reasonix Studio — " + line;
                }
                catch (Exception error) when (!token.IsCancellationRequested)
                {
                    Host.ShellLog.Line("tray: " + error.Message);
                }
            }
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(true));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }
}
