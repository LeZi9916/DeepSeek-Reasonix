using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;
using System.Text.Json.Nodes;

namespace Reasonix.Studio.Shell.Workspace;

/// <summary>The device access control and canonical window status projection.</summary>
public sealed partial class WorkspaceChromeStatus : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="WorkspaceChromeStatus"/> class.</summary>
    public WorkspaceChromeStatus()
    {
        InitializeComponent();
        InitializePresence();
        DeviceAccess.Click += (_, _) => DeviceAccessRequested?.Invoke();
    }

    /// <summary>Raised to open the device access card.</summary>
    public event Action? DeviceAccessRequested;

    /// <summary>Raised when the active host, worktree or queue changes.</summary>
    public event Action<string, bool, int, InterfaceLanguage>? StatusChanged;

    /// <summary>Projects canonical status without inventing a runtime state.</summary>
    /// <param name="host">Remote host name.</param><param name="isolated">Whether the active root is an isolated worktree.</param>
    /// <param name="queued">Canonical queued-message count.</param><param name="language">Interface language.</param>
    public void Show(string host, bool isolated, int queued, InterfaceLanguage language)
    {
        AutomationProperties.SetName(DeviceAccess, MessageCatalog.Get("remote.device_access", language));
        ToolTip.SetTip(DeviceAccess, OnlineMark.IsVisible ? MessageCatalog.Get("remote.device_access_n_online", language, ("n", OnlineCount.Text ?? string.Empty)) : AutomationProperties.GetName(DeviceAccess));
        StatusChanged?.Invoke(host, isolated, queued, language);
    }

    /// <summary>Projects the hub's online controllers without opening a pairing route.</summary>
    /// <param name="status">Canonical share read.</param><param name="language">Interface language.</param><param name="cardOpen">Whether the card already shows device changes.</param>
    public void ShowDevices(JsonObject status, InterfaceLanguage language, bool cardOpen = false)
    {
        var online = (status["cloudDevices"] as JsonArray)?.Count ?? 0;
        online += (status["devices"] as JsonArray)?.Count(device => (bool?)device?["online"] == true) ?? 0;
        OnlineCount.Text = online.ToString(System.Globalization.CultureInfo.InvariantCulture);
        OnlineMark.IsVisible = online > 0;
        ToolTip.SetTip(DeviceAccess, MessageCatalog.Get(online > 0 ? "remote.device_access_n_online" : "remote.device_access", language, ("n", OnlineCount.Text)));
        ShowPresence(status, language, cardOpen);
    }
}
