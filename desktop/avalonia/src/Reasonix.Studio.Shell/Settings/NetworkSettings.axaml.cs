using Avalonia.Automation;
using Reasonix.Studio.Contracts.Refusal;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Edits proxy settings and shows separate diagnostics for the saved configuration.</summary>
public sealed partial class NetworkSettings : SettingsFormView
{
    /// <summary>Compares the editable fields to the last saved settings.</summary>
    private Func<bool>? _dirty;

    /// <summary>Initializes a new instance of the <see cref="NetworkSettings"/> class.</summary>
    public NetworkSettings() => InitializeComponent();

    /// <summary>Gets a value indicating whether leaving settings would discard the proxy draft.</summary>
    internal bool HasChanges => _dirty?.Invoke() == true;

    /// <summary>Loads the saved configuration from its owner.</summary>
    /// <param name="api">The owning host.</param>
    /// <param name="language">The UI language.</param><param name="describe">Formats failures.</param>
    /// <param name="token">The window lifetime.</param><returns>The initial read.</returns>
    internal async Task OpenAsync(StudioPageClient api, InterfaceLanguage language, Func<Exception, string> describe, CancellationToken token)
    {
        Translate(language);
        try
        {
            var saved = await api.NetworkAsync(token).ConfigureAwait(true);
            Bind(api, saved, describe, token);
            InitialStatus.IsVisible = false;
            Editor.IsVisible = true;
        }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested)
            {
                Loading.Text = describe(error);
            }
        }
    }

    /// <summary>Binds edits and diagnostics after the initial read succeeds.</summary>
    /// <param name="api">The owning host.</param><param name="saved">Its current settings.</param>
    /// <param name="describe">Formats failures.</param><param name="token">The window lifetime.</param>
    private void Bind(StudioPageClient api, JsonObject saved, Func<Exception, string> describe, CancellationToken token)
    {
        AutomationProperties.SetName(ProbeResults, Text("models.test_results_for_saved_settings"));
        AutomationProperties.SetName(Modes, Text("models.proxy_mode"));
        Protocol.ItemsSource = new[] { "http", "https", "socks5", "socks5h" };
        var baseline = new JsonObject();
        var busy = false;
        _dirty = () => !JsonNode.DeepEquals(baseline, Draft());
        Adopt(saved);

        foreach (var mode in new[] { SystemMode, CustomMode, DirectMode })
        {
            mode.IsCheckedChanged += (_, _) => Refresh();
        }

        foreach (var field in new[] { Server, Username, Bypass })
        {
            field.TextChanged += (_, _) => Refresh();
        }

        PortNumber.TextChanged += (_, _) =>
        {
            var current = PortNumber.Text ?? string.Empty;
            var digits = new string(current.Where(char.IsAsciiDigit).ToArray());
            if (digits != current)
            {
                PortNumber.Text = digits;
            }

            Refresh();
        };
        Password.TextChanged += (_, _) =>
        {
            if (!string.IsNullOrEmpty(Password.Text))
            {
                ClearPassword.IsChecked = false;
            }

            Refresh();
        };
        ClearPassword.IsCheckedChanged += (_, _) => Refresh();
        Protocol.SelectionChanged += (_, _) => Refresh();
        Save.Click += async (_, _) =>
        {
            if (!HasChanges)
            {
                return;
            }

            await RunAsync(saving: true, async () =>
            {
                Adopt(await api.SetNetworkAsync(Draft(), token).ConfigureAwait(true));
                ProbeResults.IsVisible = false;
                Probes.ItemsSource = null;
            }).ConfigureAwait(true);
        };
        Diagnose.Click += async (_, _) => await RunAsync(saving: false, async () =>
        {
            var result = await api.DiagnoseNetworkAsync(token).ConfigureAwait(true);
            ShowProbes(result["probes"]?.AsArray());
        }).ConfigureAwait(true);

        JsonObject Draft() => new()
        {
            ["mode"] = CustomMode.IsChecked == true ? "custom" : DirectMode.IsChecked == true ? "off" : "auto",
            ["url"] = saved["url"]?.DeepClone(),
            ["type"] = Protocol.SelectedItem?.ToString(),
            ["server"] = Server.Text?.Trim(),
            ["port"] = string.IsNullOrEmpty(PortNumber.Text)
                ? 0
                : int.TryParse(PortNumber.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) ? port : int.MaxValue,
            ["username"] = Username.Text,
            ["noProxy"] = Bypass.Text,
            ["password"] = Password.Text,
            ["clearPassword"] = ClearPassword.IsChecked == true,
        };

        void Adopt(JsonObject applied)
        {
            saved = applied;
            ((string?)applied["mode"] switch
            {
                "custom" => CustomMode,
                "off" => DirectMode,
                _ => SystemMode,
            }).IsChecked = true;
            Protocol.SelectedItem = (string?)applied["type"] is { Length: > 0 } type ? type : "http";
            Server.Text = (string?)applied["server"] ?? string.Empty;
            PortNumber.Text = (int?)applied["port"] is > 0 and var port ? port.ToString(CultureInfo.InvariantCulture) : string.Empty;
            Username.Text = (string?)applied["username"] ?? string.Empty;
            Bypass.Text = (string?)applied["noProxy"] ?? string.Empty;
            Password.Text = string.Empty;
            ClearPassword.IsChecked = false;
            Effective.Text = (string?)applied["effective"] is { Length: > 0 } value ? value : "—";
            Endpoint.Text = (string?)applied["endpoint"];
            EndpointRow.IsVisible = !string.IsNullOrEmpty(Endpoint.Text);
            DirectHosts.Text = string.Join(", ", applied["direct"]?.AsArray().Select(host => (string?)host) ?? []);
            DirectRow.IsVisible = !string.IsNullOrEmpty(DirectHosts.Text);
            Password.PlaceholderText = Text((bool?)applied["hasPassword"] == true ? "models.saved_leave_blank_to_keep" : "models.you_can_write_proxy_password");
            baseline = Draft();
            Refresh();
        }

        void Refresh()
        {
            CustomFields.IsVisible = CustomMode.IsChecked == true;
            ClearPassword.IsVisible = (bool?)saved["hasPassword"] == true && string.IsNullOrEmpty(Password.Text);
            ModeNote.Text = CustomMode.IsChecked == true
                ? "手动指定代理服务器"
                : DirectMode.IsChecked == true ? "不使用任何代理" : Text("models.uses_whatever_proxy_the_system");
            Unsaved.IsVisible = HasChanges;
            Save.IsEnabled = !busy && HasChanges;
        }

        async Task RunAsync(bool saving, Func<Task> action)
        {
            if (busy)
            {
                return;
            }

            busy = true;
            Failure.IsVisible = false;
            Save.IsEnabled = Diagnose.IsEnabled = false;
            Modes.IsEnabled = CustomFields.IsEnabled = !saving;
            var button = saving ? Save : Diagnose;
            var label = button.Content;
            button.Content = Text(saving ? "common.saving" : "models.testing_2");
            try
            {
                await action().ConfigureAwait(true);
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    Failure.Text = describe(error);
                    Failure.IsVisible = true;
                }
            }
            finally
            {
                busy = false;
                button.Content = label;
                Modes.IsEnabled = CustomFields.IsEnabled = Diagnose.IsEnabled = true;
                Refresh();
            }
        }
    }

    /// <summary>Projects the diagnosed steps in the order reported by the host.</summary>
    /// <param name="result">The saved configuration probes.</param>
    private void ShowProbes(JsonArray? result)
    {
        Probes.ItemsSource = (result?.OfType<JsonObject>() ?? []).Select(probe =>
        {
            var step = (string?)probe["step"] ?? string.Empty;
            var label = step switch
            {
                "proxy" => Text("models.proxy"),
                "dns" => Text("models.name_resolution"),
                "connect" => Text("models.connection"),
                "tls" => Text("models.tls_handshake"),
                "auth" => Text("models.credentials"),
                _ => step,
            };
            var values = probe["params"]?.Deserialize<Dictionary<string, JsonElement>>();
            var duration = (long?)probe["durationMs"] ?? 0;
            return new ProbeRow(
                (bool?)probe["ok"] == true,
                label,
                Say((string?)probe["code"], (string?)probe["detail"], values),
                duration > 0 ? duration.ToString(CultureInfo.InvariantCulture) + "ms" : string.Empty,
                (string?)probe["advice"] is { Length: > 0 } advice ? Say((string?)probe["adviceCode"], advice, values) : string.Empty);
        }).ToArray();
        ProbeResults.IsVisible = true;
    }

    /// <summary>Localizes a diagnostic using its wire identity.</summary>
    /// <param name="code">The diagnostic code.</param><param name="fallback">The host's plain text.</param>
    /// <param name="values">Parameters for the translation.</param><returns>The diagnostic text.</returns>
    private string Say(string? code, string? fallback, Dictionary<string, JsonElement>? values) =>
        RefusalText.Say(new Reason { Code = code, Error = fallback, Params = values }, null, null, Language);

    /// <summary>A displayed diagnostic row.</summary>
    /// <param name="Ok">Whether the step succeeded.</param><param name="Label">The step name.</param>
    /// <param name="Detail">The result text.</param><param name="Duration">The formatted time.</param>
    /// <param name="Advice">The advice when supplied.</param>
    private sealed record ProbeRow(bool Ok, string Label, string Detail, string Duration, string Advice)
    {
        /// <summary>Gets a value indicating whether the step has a second line of advice.</summary>
        public bool HasAdvice => Advice.Length > 0;
    }
}
