using Avalonia.Threading;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;
using System.Text.Json.Nodes;

namespace Reasonix.Studio.Shell.Workspace;

public sealed partial class WorkspaceChromeStatus
{
    /// <summary>The reference leaves a connection note visible for 3.2 seconds.</summary>
    private readonly DispatcherTimer _presenceLifetime = new() { Interval = TimeSpan.FromMilliseconds(3200) };

    /// <summary>The previous acknowledged presence, copied before callers can reuse their JSON object.</summary>
    private JsonObject? _previousDevices;

    /// <summary>Dismisses an existing note when its device card opens.</summary>
    internal void HidePresence()
    {
        _presenceLifetime.Stop();
        Presence.IsOpen = false;
    }

    /// <summary>Owns the note lifetime without keeping a detached popup alive.</summary>
    private void InitializePresence()
    {
        Presence.PlacementTarget = DeviceAccess;
        _presenceLifetime.Tick += (_, _) => HidePresence();
        DetachedFromVisualTree += (_, _) => HidePresence();
    }

    /// <summary>Names actual LAN or internet device transitions after the initial canonical read.</summary>
    /// <param name="status">Fresh hub status.</param><param name="language">Interface language.</param><param name="cardOpen">Whether the card already shows the list.</param>
    private void ShowPresence(JsonObject status, InterfaceLanguage language, bool cardOpen)
    {
        var previous = _previousDevices;
        _previousDevices = status.DeepClone().AsObject();
        if (previous is null || cardOpen)
        {
            HidePresence();
            return;
        }

        JsonObject[] Devices(JsonObject owner, string key) => (owner[key] as JsonArray)?.OfType<JsonObject>().ToArray() ?? [];
        var oldCloud = Devices(previous, "cloudDevices");
        var cloud = Devices(status, "cloudDevices");
        var note = string.Empty;
        if (cloud.Any(device => oldCloud.All(old => (string?)old["id"] != (string?)device["id"])))
        {
            note = MessageCatalog.Get("remote.web_studio_connected", language);
        }
        else if (oldCloud.Any(device => cloud.All(next => (string?)next["id"] != (string?)device["id"])))
        {
            note = MessageCatalog.Get("remote.web_studio_disconnected", language);
        }

        if (note.Length == 0)
        {
            var oldLocal = Devices(previous, "devices");
            var local = Devices(status, "devices");
            for (var index = 0; index < local.Length; index++)
            {
                var device = local[index];
                var old = oldLocal.FirstOrDefault(old => (string?)old["id"] == (string?)device["id"]);
                var online = (bool?)device["online"] == true;
                var wasOnline = (bool?)old?["online"] == true;
                if (online != wasOnline)
                {
                    note = MessageCatalog.Get(online ? "remote.device_n_connected" : "remote.device_n_disconnected", language, ("n", (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
                }
            }

            for (var index = 0; index < oldLocal.Length; index++)
            {
                if (local.All(next => (string?)next["id"] != (string?)oldLocal[index]["id"]))
                {
                    note = MessageCatalog.Get("remote.device_n_disconnected", language, ("n", (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
                }
            }
        }

        if (note.Length > 0)
        {
            PresenceText.Text = note;
            Presence.IsOpen = true;
            _presenceLifetime.Stop();
            _presenceLifetime.Start();
        }
    }
}
