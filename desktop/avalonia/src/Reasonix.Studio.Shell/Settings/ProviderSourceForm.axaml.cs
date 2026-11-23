using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>The inline custom source and preset installation form.</summary>
internal sealed partial class ProviderSourceForm : UserControl
{
    /// <summary>Configured source names and endpoint identities.</summary>
    private JsonArray _known = [];

    /// <summary>The protocols advertised by the host.</summary>
    private JsonArray _catalog = [];

    /// <summary>The localized settings presentation.</summary>
    private InterfaceLanguage _language;

    /// <summary>The kernel that owns model sources and credentials.</summary>
    private StudioPageClient? _api;

    /// <summary>Cancellation for the active window.</summary>
    private CancellationToken _token;

    /// <summary>Maps kernel failures to their translated meanings.</summary>
    private Func<Exception, string>? _describe;

    /// <summary>Refreshes the settings after a source has been added.</summary>
    private Func<Task>? _saved;

    /// <summary>Closes this form and restores the previously selected service.</summary>
    private Action? _cancel;

    /// <summary>Whether a probe, test, or write is in progress.</summary>
    private bool _busy;

    /// <summary>Whether the source creation has already been acknowledged.</summary>
    private bool _created;

    /// <summary>Initializes a new instance of the <see cref="ProviderSourceForm"/> class.</summary>
    public ProviderSourceForm()
    {
        InitializeComponent();
        ActualThemeVariantChanged += (_, _) => SourceFrame.Classes.Set("light", ActualThemeVariant == ThemeVariant.Light);
        SourceFrame.Classes.Set("light", ActualThemeVariant == ThemeVariant.Light);
    }

    /// <summary>Gets the configured protocol represented by the current choice.</summary>
    private JsonObject? Wire => _catalog.OfType<JsonObject>().FirstOrDefault(item => (string?)item["kind"] == SelectedId(Protocol));

    /// <summary>Connects the inline source form to the settings host.</summary>
    /// <param name="api">Kernel client.</param>
    /// <param name="known">Current sources.</param>
    /// <param name="catalog">Advertised protocol catalogue.</param>
    /// <param name="language">Interface language.</param>
    /// <param name="token">Window lifetime.</param>
    /// <param name="describe">Localized kernel errors.</param>
    /// <param name="saved">Read-after-write callback.</param>
    /// <param name="cancel">Closes the editor.</param>
    internal void Open(
        StudioPageClient api,
        JsonArray known,
        JsonArray catalog,
        InterfaceLanguage language,
        CancellationToken token,
        Func<Exception, string> describe,
        Func<Task> saved,
        Action cancel)
    {
        _api = api;
        _known = known;
        _language = language;
        _token = token;
        _describe = describe;
        _saved = saved;
        _cancel = cancel;
        LabelForm();
        SetProtocols(catalog);
        SetPreset(null);
        Close.Click += (_, _) => CancelForm();
        Cancel.Click += (_, _) => CancelForm();
        Back.Click += (_, _) => SetPreset(null);
        TogglePresets.Click += (_, _) =>
        {
            PresetsBody.IsVisible = !PresetsBody.IsVisible;
            if (PresetsBody.IsVisible)
            {
                DrawPresets();
            }
        };
        PresetSearch.TextChanged += (_, _) => DrawPresets();
        AdvancedHeader.Click += (_, _) =>
        {
            AdvancedBody.IsVisible = !AdvancedBody.IsVisible;
            AdvancedChevron.RenderTransform = AdvancedBody.IsVisible ? new RotateTransform(180) : null;
        };
        Endpoint.TextChanged += (_, _) =>
        {
            if (_normalizedAddress != Endpoint.Text)
            {
                ClearProbe();
            }
        };
        Credential.TextChanged += (_, _) => ClearProbe();
        Protocol.SelectionChanged += (_, _) => ClearChecks();
        BypassProxy.IsCheckedChanged += (_, _) => ClearChecks();
        Thinking.IsCheckedChanged += (_, _) => UpdateState();
        ThinkingProtocol.SelectionChanged += (_, _) => UpdateState();
        foreach (var field in new[] { SourceName, ContextWindow, OutputLimit, Extra, PresetName })
        {
            field.TextChanged += (_, _) => UpdateState();
        }

        ModelSearch.TextChanged += (_, _) => DrawModels();
        ModelSearch.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && AddModel.IsVisible && AddModel.IsEnabled)
            {
                e.Handled = true;
                AppendModel();
            }
        };
        AddModel.Click += (_, _) => AppendModel();
        Probe.Click += async (_, _) => await ProbeAsync().ConfigureAwait(true);
        TestModels.Click += async (_, _) => await TestAsync(_picked.ToArray()).ConfigureAwait(true);
        Save.Click += async (_, _) => await SaveAsync().ConfigureAwait(true);
        UpdateState();
        _ = LoadPresetsAsync();
    }

    /// <summary>Updates the available protocols without changing the current selection.</summary>
    /// <param name="catalog">The kernel protocol list.</param>
    internal void SetProtocols(JsonArray catalog)
    {
        _catalog = catalog;
        var selection = SelectedId(Protocol);
        var choices = catalog.OfType<JsonObject>().Where(item => (string?)item["kind"] is { Length: > 0 })
            .Select(item => Choice((string)item["kind"]!, ProtocolName((string)item["kind"]!))).ToArray();
        Protocol.ItemsSource = choices;
        Protocol.SelectedItem = choices.FirstOrDefault(item => Equals(item.Tag, selection))
            ?? choices.FirstOrDefault(item => Equals(item.Tag, "openai")) ?? choices.FirstOrDefault();
        UpdateState();
    }

    /// <summary>Places preset cards at the reference minimum width across the available grid.</summary>
    /// <param name="e">New fieldset size.</param>
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (PresetsBody.IsVisible && Math.Abs(e.PreviousSize.Width - e.NewSize.Width) >= 1)
        {
            DrawPresets();
        }
    }

    /// <summary>Gets a catalogue option's stable wire value.</summary>
    /// <param name="combo">The choice control.</param>
    /// <returns>The selected value.</returns>
    private static string SelectedId(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;

    /// <summary>Creates one localized choice while retaining its protocol identifier.</summary>
    /// <param name="id">The wire identifier.</param>
    /// <param name="label">Visible choice.</param>
    /// <returns>The combo item.</returns>
    private static ComboBoxItem Choice(string id, string label) => new() { Tag = id, Content = label };

    /// <summary>Reads name rules without rewriting what the reader typed.</summary>
    /// <returns>The kernel-compatible source name pattern.</returns>
    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SourceNamePattern();

    /// <summary>Returns the credential-sharing host, including a non-default port.</summary>
    /// <param name="value">Endpoint address.</param>
    /// <returns>Host and port when present.</returns>
    private static string Host(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var address)
        ? address.Authority.ToLowerInvariant()
        : value?.Trim().ToLowerInvariant() ?? string.Empty;

    /// <summary>Returns a token limit or zero for an empty value.</summary>
    /// <param name="input">Token-limit editor.</param>
    /// <returns>The parsed nonnegative limit, or null for invalid input.</returns>
    private static int? Limit(TextBox input) => string.IsNullOrWhiteSpace(input.Text)
        ? 0
        : int.TryParse(input.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) ? limit : null;

    /// <summary>Reads custom header lines using the same syntax as Electron.</summary>
    /// <param name="text">One header per line.</param>
    /// <returns>The declared header names and values.</returns>
    private static JsonObject ReadHeaders(string? text)
    {
        var result = new JsonObject();
        foreach (var line in (text ?? string.Empty).Split('\n'))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0 && line[..colon].Trim() is { Length: > 0 } key && line[(colon + 1)..].Trim() is { Length: > 0 } value)
            {
                result[key] = value;
            }
        }

        return result;
    }

    /// <summary>Reads a JSON object for optional request fields.</summary>
    /// <param name="text">Draft request body.</param>
    /// <returns>The object, or null when invalid.</returns>
    private static JsonObject? ReadExtra(string? text)
    {
        try
        {
            return string.IsNullOrWhiteSpace(text) ? new JsonObject() : JsonNode.Parse(text) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Supplies the reference field labels and helper strings.</summary>
    private void LabelForm()
    {
        ProtocolLabel.Text = Text("models.api_protocol");
        ProtocolHint.Text = Text("models.follow_the_provider_documentation_a");
        EndpointLabel.Text = Text("models.endpoint_address");
        DecisionHint.Text = Text("models.enter_the_services_base_address");
        CredentialHint.Text = Text("common.the_api_key_is_stored");
        NameLabel.Text = Text("common.source_name");
        SourceName.PlaceholderText = Text("models.made_after_connecting_editable");
        LimitsTitle.Text = Text("models.model_limits");
        LimitsHint.Text = Text("models.defaults_for_models_from_this");
        WindowLabel.Text = Text("common.context_window");
        WindowHint.Text = Text("models.the_shared_capacity_for_input");
        OutputLabel.Text = Text("models.maximum_output");
        OutputLimit.PlaceholderText = Text("common.auto");
        OutputHint.Text = Text("models.the_per_turn_reply_limit");
        AdvancedTitle.Text = Text("models.advanced_connection_options");
        AdvancedHint.Text = Text("models.thinking_controls_proxy_and_gateway");
        Optional.Text = Text("common.optional");
        ThinkingTitle.Text = Text("models.send_thinking_controls");
        ThinkingProtocolLabel.Text = Text("models.thinking_protocol");
        ThinkingProtocolHint.Text = Text("models.use_auto_to_follow_the");
        ProxyTitle.Text = Text("models.bypass_system_proxy");
        ProxyHint.Text = Text("models.enable_only_when_the_endpoint");
        HeadersLabel.Text = Text("models.extra_request_headers");
        Headers.PlaceholderText = "HTTP-Referer: https://example.com\nX-Title: Reasonix";
        HeadersHint.Text = Text("models.one_name_value_pair_per");
        ExtraLabel.Text = Text("models.extra_request_body");
        Extra.PlaceholderText = "{\n  \"temperature\": 0.7\n}";
        ExtraHint.Text = Text("models.only_for_optional_fields_required");
        ExtraError.Text = Text("models.enter_a_valid_json_object");
        CatalogueTitle.Text = Text("models.model_catalogue");
        ProbeFailure.Text = Text("models.unreachable");
        EnabledTitle.Text = Text("models.enabled_models");
        CatalogueGuide.Text = Text("models.the_catalog_is_for_discovery");
        ModelSearch.PlaceholderText = Text("composer.search_or_enter_an_exact");
        AddModelHint.Text = Text("composer.saves_the_exact_model_id");
        PresetsTitle.Text = Text("persona.start_from_a_common_service");
        PresetSearch.PlaceholderText = Text("persona.search_services");
        PresetNameLabel.Text = Text("common.source_name");
        PresetName.PlaceholderText = Text("persona.the_presets_own_name");
        PresetNameHint.Text = Text("persona.leave_empty_to_use_the");
        PresetPrivacy.Text = CredentialHint.Text;
        Cancel.Content = Text("common.cancel");
        Back.Content = Text("common.new_code");
        AutomationProperties.SetName(Close, Text("common.cancel"));
        AutomationProperties.SetName(ModelSearch, Text("composer.search_or_add_a_model"));
        foreach (var (control, caption) in new (Control, TextBlock)[]
        {
            (Protocol, ProtocolLabel), (Endpoint, EndpointLabel), (Credential, CredentialLabel), (SourceName, NameLabel),
            (ContextWindow, WindowLabel), (OutputLimit, OutputLabel), (Thinking, ThinkingTitle), (BypassProxy, ProxyTitle),
            (Headers, HeadersLabel), (Extra, ExtraLabel), (ThinkingProtocol, ThinkingProtocolLabel), (PresetName, PresetNameLabel),
        })
        {
            AutomationProperties.SetName(control, caption.Text);
        }

        AutomationProperties.SetName(PresetCredential, "API Key");
        ThinkingProtocol.ItemsSource = new[]
        {
            Choice(string.Empty, Text("app.auto_inferred_from_the_model")),
            Choice("openai", Text("app.openai_style")),
            Choice("anthropic", "Anthropic thinking"),
            Choice("deepseek", "DeepSeek"),
            Choice("glm", "GLM enable_thinking"),
            Choice("kimi-k3", "Kimi K3"),
        };
        ThinkingProtocol.SelectedIndex = 0;
    }

    /// <summary>Updates validation, per-protocol fields, and the enabled actions.</summary>
    private void UpdateState()
    {
        var wire = Wire;
        var kind = SelectedId(Protocol);
        var endpoint = Endpoint.Text?.Trim() ?? string.Empty;
        var sibling = endpoint.Length > 0 && _known.OfType<JsonObject>().Any(item => Host((string?)item["baseUrl"]) == Host(endpoint));
        var name = (_preset is null ? SourceName.Text : PresetName.Text)?.Trim() ?? string.Empty;
        var validName = name.Length == 0 ? _preset is not null : SourceNamePattern().IsMatch(name);
        var badName = name.Length > 0 && !validName;
        NameHint.Classes.Set("error", _preset is null && badName);
        PresetNameHint.Classes.Set("error", _preset is not null && badName);
        NameHint.Text = Text("models.use_only_letters_digits_dots");
        var validExtra = ReadExtra(Extra.Text) is not null;
        ExtraError.IsVisible = !validExtra;
        Save.Content = Text(_busy ? "common.saving" : "common.add_source");
        Save.IsEnabled = !_busy && !_created && validName && (_preset is not null || (endpoint.Length > 0 && kind.Length > 0
            && _picked.Count > 0 && validExtra && Limit(ContextWindow) is not null && Limit(OutputLimit) is not null));
        SourceFields.IsEnabled = PresetFields.IsEnabled = Cancel.IsEnabled = Back.IsEnabled = Close.IsEnabled = !_busy;
        var canDiscover = (string?)wire?["discovery"] is { Length: > 0 };
        Probe.IsVisible = canDiscover;
        Probe.IsEnabled = !_busy && endpoint.Length > 0;
        Probe.Content = Text(_busy ? "models.checking_2" : "models.check_connection_and_read_models");
        CatalogueHint.Text = Text(canDiscover ? "models.add_at_least_one_model" : "models.this_protocol_has_no_model");
        TestModels.IsEnabled = !_busy && endpoint.Length > 0 && _picked.Count > 0;
        var canThink = (bool?)wire?["reasoningParams"] == true;
        Thinking.IsEnabled = canThink;
        ThinkingHint.Text = Text(canThink ? "models.allow_this_protocol_to_send" : "models.this_protocol_has_no_thinking");
        ThinkingSection.IsVisible = canThink && Thinking.IsChecked == true;
        var effort = (string?)wire?["effortField"];
        var restrictions = wire?["effortUnder"] as JsonArray;
        ThinkingShape.IsVisible = effort is { Length: > 0 } && (restrictions is not { Count: > 0 }
            || restrictions.Any(item => (string?)item == SelectedId(ThinkingProtocol)));
        ThinkingShape.Text = effort;
        BypassProxy.IsEnabled = _probe?.NoProxy != true;
        DecisionHint.IsVisible = (string?)wire?["answers"] == "decision";
        PresetGallery.IsVisible = endpoint.Length == 0 && _presets.Count > 0;
        PresetNotice.IsVisible = endpoint.Length == 0 && _presets.Count == 0 && !string.IsNullOrEmpty(PresetNotice.Text);
        Credential.PlaceholderText = sibling ? "········" : string.Empty;
        CredentialLabel.Text = "API Key" + (sibling ? Text("models.leave_blank_to_keep_the") : string.Empty);
        SiblingHint.IsVisible = sibling;
        SiblingHint.Text = MessageCatalog.Get("models.this_address_already_holds_name", _language, ("name", Host(endpoint)))
            + Text("models.this_adds_a_second_way");
    }

    /// <summary>Writes the confirmed custom source or curated preset through the kernel.</summary>
    /// <returns>The save and provider readback.</returns>
    private async Task SaveAsync()
    {
        if (_busy || _created || !Save.IsEnabled || _api is null)
        {
            return;
        }

        await RunAsync(
            async () =>
            {
                if (_preset is { } preset)
                {
                    await _api.InstallProviderPresetAsync(
                        (string)preset["id"]!,
                        PresetName.Text?.Trim() ?? string.Empty,
                        PresetCredential.Text?.Trim() ?? string.Empty,
                        _token).ConfigureAwait(true);
                }
                else
                {
                    var draft = ConnectionDraft();
                    draft["name"] = SourceName.Text?.Trim();
                    draft["models"] = new JsonArray(_picked.Select(model => (JsonNode?)JsonValue.Create(model)).ToArray());
                    draft["default"] = _picked[0];
                    draft["effort"] = string.Empty;
                    draft["contextWindow"] = Limit(ContextWindow);
                    draft["maxOutputTokens"] = Limit(OutputLimit);
                    draft["vision"] = new JsonArray((_probe?.Vision ?? []).Where(_picked.Contains).Select(model => (JsonNode?)JsonValue.Create(model)).ToArray());
                    if ((bool?)Wire?["reasoningParams"] == true)
                    {
                        draft["reasoningProtocol"] = Thinking.IsChecked == true ? SelectedId(ThinkingProtocol) : "none";
                    }

                    draft["headers"] = ReadHeaders(Headers.Text);
                    draft["extraBody"] = ReadExtra(Extra.Text);
                    await _api.AddProviderAsync(draft, _token).ConfigureAwait(true);
                }

                _created = true;
                Credential.Text = PresetCredential.Text = string.Empty;
                await _saved!().ConfigureAwait(true);
            },
            SaveError).ConfigureAwait(true);
    }

    /// <summary>Creates the wire fields shared by a draft model test and source creation.</summary>
    /// <returns>A standalone connection payload.</returns>
    private JsonObject ConnectionDraft() => new()
    {
        ["baseUrl"] = Endpoint.Text?.Trim(),
        ["kind"] = SelectedId(Protocol),
        ["apiKey"] = Credential.Text?.Trim() ?? string.Empty,
        ["authHeader"] = _probe?.AuthHeader == true,
        ["noProxy"] = BypassProxy.IsChecked == true || _probe?.NoProxy == true,
    };

    /// <summary>Serializes form actions and displays a request failure in its own section.</summary>
    /// <param name="action">Operation using the current draft.</param>
    /// <param name="status">The local result or error field.</param>
    /// <returns>The operation.</returns>
    private async Task RunAsync(Func<Task> action, TextBlock status)
    {
        _busy = true;
        if (status == ProbeStatus)
        {
            ProbeMessage.IsVisible = false;
        }
        else
        {
            status.IsVisible = false;
        }

        UpdateState();
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception error)
        {
            if (!_token.IsCancellationRequested)
            {
                status.Classes.Remove("success");
                status.Text = _describe!(error);
                if (status == ProbeStatus)
                {
                    ProbeMessage.Classes.Add("error");
                    ProbeFailure.IsVisible = ProbeMessage.IsVisible = true;
                }
                else
                {
                    status.IsVisible = true;
                }
            }
        }
        finally
        {
            _busy = false;
            DrawModels();
            UpdateState();
        }
    }

    /// <summary>Returns to the service list without writing the draft.</summary>
    private void CancelForm()
    {
        if (!_busy)
        {
            Credential.Text = PresetCredential.Text = string.Empty;
            _cancel?.Invoke();
        }
    }

    /// <summary>Suggests an unused config key for the probed host.</summary>
    /// <param name="host">Remote provider hostname.</param>
    /// <returns>The derived name or its first available numeric suffix.</returns>
    private string SuggestedName(string host)
    {
        var clean = Regex.Replace(Regex.Replace(host, "^(www|api|open|gateway)\\.", string.Empty).Split('.')[0], "[^a-zA-Z0-9._-]", "-");
        if (!SourceNamePattern().IsMatch(clean))
        {
            clean = "custom";
        }

        var used = _known.OfType<JsonObject>().Select(ProviderServiceAccount.Name).ToHashSet(StringComparer.Ordinal);
        for (var index = 2; used.Contains(clean); index++)
        {
            var suffix = "-" + index.ToString(CultureInfo.InvariantCulture);
            var root = Regex.Replace(clean, "-[0-9]+$", string.Empty);
            clean = root[..Math.Min(root.Length, 64 - suffix.Length)] + suffix;
        }

        return clean;
    }

    /// <summary>Uses the same protocol labels as the service detail.</summary>
    /// <param name="kind">Wire identifier.</param>
    /// <returns>Localized label.</returns>
    private string ProtocolName(string kind) => ProviderDetailSheet.ProtocolName(kind, _language);

    /// <summary>Loads a caption in the active interface language.</summary>
    /// <param name="key">Catalog key.</param>
    /// <returns>Caption text.</returns>
    private string Text(string key) => MessageCatalog.Get(key, _language);
}
