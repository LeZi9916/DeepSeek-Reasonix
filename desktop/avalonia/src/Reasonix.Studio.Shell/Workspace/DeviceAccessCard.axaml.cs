using Avalonia.Controls;
using Avalonia.Input.Platform;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Workspace;

/// <summary>The device access card backed by the hub's existing share surface.</summary>
public sealed partial class DeviceAccessCard : UserControl
{
    /// <summary>The API belonging to the local window.</summary>
    private StudioPageClient? _api;

    /// <summary>The requested interface language.</summary>
    private InterfaceLanguage _language;

    /// <summary>The last acknowledged device access status.</summary>
    private JsonObject? _status;

    /// <summary>The local pairing link issued by the hub.</summary>
    private string _localLink = string.Empty;

    /// <summary>The internet pairing link issued by the hub.</summary>
    private string _cloudLink = string.Empty;

    /// <summary>Prevents acknowledgement projection from writing back.</summary>
    private bool _showing;

    /// <summary>Initializes a new instance of the <see cref="DeviceAccessCard"/> class.</summary>
    public DeviceAccessCard()
    {
        InitializeComponent();
        AllowLocal.IsCheckedChanged += async (_, _) =>
        {
            if (!_showing)
            {
                await RunAsync(async () =>
                {
                    _status = await _api!.DeviceAccessActionAsync(AllowLocal.IsChecked == true ? "open" : "close", new JsonObject { ["ip"] = Address.SelectedItem?.ToString() ?? string.Empty }, CancellationToken.None).ConfigureAwait(true);
                    ShowStatus();
                    if (AllowLocal.IsChecked == true)
                    {
                        await OfferAsync(false).ConfigureAwait(true);
                    }
                }).ConfigureAwait(true);
            }
        };
        Address.SelectionChanged += async (_, _) =>
        {
            if (!_showing && _status is not null && (bool?)_status["open"] == true)
            {
                await RunAsync(async () =>
                {
                    _status = await _api!.DeviceAccessActionAsync("open", new JsonObject { ["ip"] = Address.SelectedItem?.ToString() ?? string.Empty }, CancellationToken.None).ConfigureAwait(true);
                    await OfferAsync(false).ConfigureAwait(true);
                }).ConfigureAwait(true);
            }
        };
        NewLocal.Click += async (_, _) => await RunAsync(() => OfferAsync(false)).ConfigureAwait(true);
        MintLocal.Click += async (_, _) => await RunAsync(() => OfferAsync(false)).ConfigureAwait(true);
        NewCloud.Click += async (_, _) => await RunAsync(() => OfferAsync(true)).ConfigureAwait(true);
        CopyLocal.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(_localLink).ConfigureAwait(true);
            }
        };
        CopyCloud.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(_cloudLink).ConfigureAwait(true);
            }
        };
    }

    /// <summary>Raised after the share surface acknowledges a device change.</summary>
    public event Action<JsonObject>? StatusChanged;

    /// <summary>Projects a fresh canonical device read without requesting another pairing code.</summary>
    /// <param name="status">Acknowledged hub device status.</param>
    public void ShowStatus(JsonObject status)
    {
        _status = status;
        ShowStatus();
    }

    /// <summary>Reads the real device list and offers a code only for an already enabled route.</summary>
    /// <param name="api">Local hub API.</param><param name="language">Interface language.</param>
    /// <returns>The acknowledged device card.</returns>
    public async Task OpenAsync(StudioPageClient api, InterfaceLanguage language)
    {
        _api = api;
        _language = language;
        Title.Text = T("remote.use_studio_on_your_phone");
        Subtitle.Text = T("remote.scan_with_your_phone_to");
        ReachLabel.Text = T("remote.how_the_phone_reaches_this");
        LocalLabel.Text = T("remote.same_wifi");
        CloudLabel.Text = T("remote.from_anywhere");
        NewLocal.Content = T("common.new_code");
        MintLocal.Content = T("remote.show_local_network_qr_code");
        NewCloud.Content = T("remote.refresh_qr_code");
        CopyLocal.Content = CopyCloud.Content = T("remote.copy_link");
        await RunAsync(async () =>
        {
            _status = await api.DeviceAccessAsync(CancellationToken.None).ConfigureAwait(true);
            ShowStatus();
            if ((bool?)_status?["cloudRemote"]?["online"] == true)
            {
                await OfferAsync(true).ConfigureAwait(true);
            }
            else if ((bool?)_status?["open"] == true)
            {
                await OfferAsync(false).ConfigureAwait(true);
            }
        }).ConfigureAwait(true);
    }

    /// <summary>Reads an existing localized label.</summary>
    /// <param name="key">Message identity.</param><returns>The localized label.</returns>
    private string T(string key) => MessageCatalog.Get(key, _language);

    /// <summary>Projects only acknowledged share and device state.</summary>
    private void ShowStatus()
    {
        _showing = true;
        try
        {
            AllowLocal.IsEnabled = _status?["addresses"] is JsonArray { Count: > 0 };
            AllowLocal.IsChecked = (bool?)_status?["open"] == true;
            Address.ItemsSource = (_status?["addresses"] as JsonArray)?.Select(address => (string?)address?["ip"] ?? string.Empty).ToArray();
            Address.SelectedIndex = 0;
            Address.IsVisible = Address.ItemCount > 1;
            if (AllowLocal.IsChecked != true || string.IsNullOrEmpty((string?)_status?["offerExpires"]))
            {
                _localLink = string.Empty;
                LocalQr.Show(string.Empty);
            }

            LocalCode.IsVisible = AllowLocal.IsChecked == true && _localLink.Length > 0;
            MintLocal.IsVisible = AllowLocal.IsChecked == true && _localLink.Length == 0;
            NewCloud.IsVisible = (bool?)_status?["cloudRemote"]?["online"] == true;
            if (!NewCloud.IsVisible)
            {
                _cloudLink = string.Empty;
                CloudQr.Show(string.Empty);
            }

            CloudQr.IsVisible = CopyCloud.IsVisible = NewCloud.IsVisible && _cloudLink.Length > 0;
            var reason = (string?)_status?["cloudRemote"]?["reason"];
            var unavailable = reason switch
            {
                "relay_unreachable" => "remote.the_relay_cannot_be_reached",
                "relay_refused" => "remote.the_relay_refused_this_devices",
                _ => "remote.sign_in_to_reasonix_to",
            };
            CloudNote.Text = T(NewCloud.IsVisible ? "remote.internet_access_requires_the_same" : unavailable);
            ShowDeviceRows();
            if (_status is not null)
            {
                StatusChanged?.Invoke(_status);
            }
        }
        finally
        {
            _showing = false;
        }
    }

    /// <summary>Draws the hub-issued code and preserves its returned link.</summary>
    /// <param name="cloud">Whether to request the internet route.</param><returns>The actual offer.</returns>
    private async Task OfferAsync(bool cloud)
    {
        var offer = await _api!.DeviceAccessActionAsync(cloud ? "cloud-offer" : "offer", new JsonObject(), CancellationToken.None).ConfigureAwait(true);
        _status = await _api.DeviceAccessAsync(CancellationToken.None).ConfigureAwait(true);
        var qr = (string?)offer["qr"] ?? string.Empty;
        if (cloud)
        {
            _cloudLink = (string?)offer["url"] ?? string.Empty;
            CloudQr.Show(qr);
            CloudQr.IsVisible = CopyCloud.IsVisible = true;
        }
        else
        {
            _localLink = (string?)offer["url"] ?? string.Empty;
            LocalQr.Show(qr);
            Expires.Text = (string?)offer["expires"] ?? string.Empty;
        }

        ShowStatus();
    }

    /// <summary>Keeps a failed action visible without claiming an enabled route.</summary>
    /// <param name="run">The acknowledged HTTP operation.</param><returns>The completed interaction.</returns>
    private async Task RunAsync(Func<Task> run)
    {
        try
        {
            Failure.IsVisible = false;
            IsEnabled = false;
            await run().ConfigureAwait(true);
        }
        catch (Exception error)
        {
            Failure.Text = error.Message;
            Failure.IsVisible = true;
            ShowStatus();
        }
        finally
        {
            IsEnabled = true;
        }
    }
}
