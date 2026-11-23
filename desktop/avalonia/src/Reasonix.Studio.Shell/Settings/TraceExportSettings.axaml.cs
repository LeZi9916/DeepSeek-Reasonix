using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>The host-owned trace export fields, saved individually on commit.</summary>
internal sealed partial class TraceExportSettings : UserControl
{
    /// <summary>The active runtime's settings client.</summary>
    private readonly StudioPageClient _api;

    /// <summary>The language used for configuration status.</summary>
    private readonly InterfaceLanguage _language;

    /// <summary>Translates a save refusal.</summary>
    private readonly Func<Exception, string> _describe;

    /// <summary>Cancels operations when leaving the account page.</summary>
    private readonly CancellationToken _token;

    /// <summary>The latest configuration acknowledged by the host.</summary>
    private JsonObject _saved = new();

    /// <summary>Whether another settings write is already in progress.</summary>
    private bool _busy;

    /// <summary>Whether the active host declined editing.</summary>
    private bool _locked;

    /// <summary>Initializes a new instance of the <see cref="TraceExportSettings"/> class.</summary>
    /// <param name="api">The owning runtime.</param><param name="language">The window language.</param>
    /// <param name="describe">A translator for host errors.</param><param name="token">Cancels on departure.</param>
    internal TraceExportSettings(StudioPageClient api, InterfaceLanguage language, Func<Exception, string> describe, CancellationToken token)
    {
        _api = api;
        _language = language;
        _describe = describe;
        _token = token;
        InitializeComponent();
        ExportLabel.Text = Text("data.export_traces");
        AutomationProperties.SetName(Enabled, ExportLabel.Text);
        SaveNote.Text = Text("data.a_save_applies_to_sessions");
        ReadOnlyNote.Text = Text("common.these_settings_are_read_only");
        WireField("endpoint", "data.collector_address", EndpointLabel, Endpoint);
        WireField("headerName", "data.auth_header_name", HeaderNameLabel, HeaderName);
        WireField("headerEnv", "data.environment_variable_holding_the_credential", HeaderEnvLabel, HeaderEnv);
        WireField("serviceName", "data.service_name", ServiceNameLabel, ServiceName);
        Enabled.Click += async (_, _) =>
        {
            var value = Enabled.IsChecked == true;
            Enabled.IsChecked = (bool?)_saved["enabled"] == true;
            await ApplyAsync("enabled", JsonValue.Create(value)).ConfigureAwait(true);
        };
    }

    /// <summary>Reads the optional observability settings.</summary>
    /// <returns>Whether the group is available on this host.</returns>
    internal async Task<bool> OpenAsync()
    {
        try
        {
            var data = await _api.TraceExportAsync(_token).ConfigureAwait(true);
            _token.ThrowIfCancellationRequested();
            Draw(data);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Connects an address or metadata field to its individual host patch.</summary>
    /// <param name="name">The property name in the host contract.</param><param name="labelKey">The caption message.</param>
    /// <param name="label">The visible field caption.</param><param name="field">The text editor.</param>
    private void WireField(string name, string labelKey, TextBlock label, TextBox field)
    {
        label.Text = Text(labelKey);
        AutomationProperties.SetName(field, label.Text);
        field.LostFocus += async (_, _) =>
        {
            var value = field.Text?.Trim() ?? string.Empty;
            if (value != ((string?)_saved[name] ?? string.Empty))
            {
                await ApplyAsync(name, JsonValue.Create(value)).ConfigureAwait(true);
            }
        };
    }

    /// <summary>Serializes saves and adopts the authoritative response.</summary>
    /// <param name="key">The single property to update.</param><param name="value">The value submitted by the reader.</param>
    /// <returns>The settings acknowledgement.</returns>
    private async Task ApplyAsync(string key, JsonNode? value)
    {
        if (_busy || _locked || _token.IsCancellationRequested)
        {
            return;
        }

        _busy = true;
        Enabled.IsEnabled = Fields.IsEnabled = false;
        Error.IsVisible = false;
        try
        {
            var result = await _api.SaveTraceExportAsync(new JsonObject { [key] = value }, _token).ConfigureAwait(true);
            _token.ThrowIfCancellationRequested();
            Draw(result);
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            _locked = error is KernelRefusal { Code: "observability.editing_disabled" };
            ReadOnlyNote.IsVisible = _locked;
            Error.Text = _describe(error);
            Error.IsVisible = true;
        }
        finally
        {
            _busy = false;
            Enabled.IsEnabled = Fields.IsEnabled = !_locked;
        }
    }

    /// <summary>Displays exactly the settings and credential-presence flag returned by the host.</summary>
    /// <param name="data">The saved settings.</param>
    private void Draw(JsonObject data)
    {
        _saved = data;
        Enabled.IsChecked = (bool?)data["enabled"] == true;
        Endpoint.Text = (string?)data["endpoint"] ?? string.Empty;
        HeaderName.Text = (string?)data["headerName"] ?? string.Empty;
        HeaderEnv.Text = (string?)data["headerEnv"] ?? string.Empty;
        ServiceName.Text = (string?)data["serviceName"] ?? string.Empty;
        ConfigPath.Text = (string?)data["path"] ?? string.Empty;
        ExportState.Text = Text((Enabled.IsChecked == true, Endpoint.Text.Length > 0) switch
        {
            (false, _) => "data.off_now_nothing_is_sent",
            (_, false) => "data.on_but_there_is_no",
            _ => "data.on_sessions_opened_from_now",
        });
        EnvNote.Text = Text((HeaderEnv.Text.Length > 0, (bool?)data["headerEnvSet"] == true) switch
        {
            (false, _) => "data.the_credential_is_never_in",
            (_, true) => "data.the_variable_is_set_its",
            _ => "data.the_variable_has_no_value",
        });
    }

    /// <summary>Translates a diagnostics caption.</summary>
    /// <param name="key">The message key.</param><returns>The localized caption.</returns>
    private string Text(string key) => MessageCatalog.Get(key, _language);
}
