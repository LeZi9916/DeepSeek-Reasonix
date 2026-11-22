using Avalonia.Automation;
using Avalonia.Controls;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;

namespace Reasonix.Studio.Shell.Workspace;

public sealed partial class DeviceAccessCard
{
    /// <summary>Projects cloud controllers and enabled LAN pairings through the same acknowledged revoke operation.</summary>
    private void ShowDeviceRows()
    {
        Devices.Children.Clear();
        var cloud = (_status?["cloudDevices"] as JsonArray)?.OfType<JsonObject>().ToArray() ?? [];
        var local = (bool?)_status?["open"] == true ? (_status?["devices"] as JsonArray)?.OfType<JsonObject>().ToArray() ?? [] : [];
        var online = cloud.Length + local.Count(device => (bool?)device["online"] == true);
        ConnectedCount.Text = T("remote.n_phones_connected").Replace("{n}", online.ToString(System.Globalization.CultureInfo.InvariantCulture), System.StringComparison.Ordinal);
        ConnectedCount.IsVisible = cloud.Length + local.Length > 0;
        foreach (var device in cloud)
        {
            AddDevice(device, (int?)device["ordinal"] ?? 1, ((string?)device["name"] ?? string.Empty) + " · " + T("remote.from_anywhere"));
        }

        for (var i = 0; i < local.Length; i++)
        {
            var device = local[i];
            AddDevice(device, i + 1, T("remote.same_wifi") + " · " + T((bool?)device["online"] == true ? "remote.online" : "remote.offline"));
        }
    }

    /// <summary>Names a device by its canonical ordinal and preserves the actual controller identifier.</summary>
    /// <param name="device">Canonical controller.</param><param name="ordinal">The hub's device number.</param><param name="detail">Route and presence.</param>
    private void AddDevice(JsonObject device, int ordinal, string detail)
    {
        var label = T("remote.device_n").Replace("{n}", ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture), System.StringComparison.Ordinal);
        var button = new Button { Content = T("remote.disconnect"), Tag = false, FontSize = 11, Padding = new Avalonia.Thickness(6, 3) };
        AutomationProperties.SetAutomationId(button, "action:share.ask-revoke");
        AutomationProperties.SetName(button, T("remote.disconnect") + " " + label);
        button.Click += async (_, _) =>
        {
            if (button.Tag is false)
            {
                button.Tag = true;
                button.Content = T("remote.disconnect_2");
                AutomationProperties.SetAutomationId(button, "action:share.revoke");
                return;
            }

            await RunAsync(async () =>
            {
                _status = await _api!.DeviceAccessActionAsync("revoke", new JsonObject { ["id"] = (string?)device["id"] }, CancellationToken.None).ConfigureAwait(true);
                ShowStatus();
            }).ConfigureAwait(true);
        };
        var name = new TextBlock { Text = label, FontSize = 12 };
        ToolTip.SetTip(name, (string?)device["name"]);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        row.Children.Add(new StackPanel { Spacing = 3, Children = { name, new TextBlock { Text = detail, FontSize = 11, TextWrapping = Avalonia.Media.TextWrapping.Wrap } } });
        Grid.SetColumn(button, 1);
        row.Children.Add(button);
        Devices.Children.Add(row);
    }
}
