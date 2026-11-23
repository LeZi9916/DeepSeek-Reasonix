using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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

/// <summary>The selected service protocol and the settings for its own connection.</summary>
internal sealed partial class ProviderDetailSheet : UserControl
{
    /// <summary>The editable per-model limits.</summary>
    private readonly Dictionary<string, (TextBox Window, TextBox Output)> _limits = new(StringComparer.Ordinal);

    /// <summary>The configured wire for the selected entry.</summary>
    private string? _storedProtocol;

    /// <summary>Whether the selected protocol belongs to an account with no sibling entry.</summary>
    private bool _allowRetype;

    /// <summary>Initializes a new instance of the <see cref="ProviderDetailSheet"/> class.</summary>
    public ProviderDetailSheet() => InitializeComponent();

    /// <summary>Gets a value indicating whether the form differs from the stored configuration.</summary>
    internal bool HasChanges
    {
        get;
        private set;
    }

    /// <summary>Names a protocol using the frontend's catalog labels.</summary>
    /// <param name="kind">Kernel protocol identifier.</param>
    /// <param name="language">The settings language.</param>
    /// <returns>The localized protocol name.</returns>
    internal static string ProtocolName(string kind, InterfaceLanguage language) => kind switch
    {
        "openai" => MessageCatalog.Get("app.openai_compatible", language),
        "anthropic" => MessageCatalog.Get("app.anthropic_compatible", language),
        "gemini" => MessageCatalog.Get("app.gemini_native_experimental", language),
        "responses" => "Responses",
        "typesafe" => MessageCatalog.Get("app.decision_system_one", language),
        "extension" => MessageCatalog.Get("ext.extension", language),
        _ => kind,
    };

    /// <summary>Stacks the actions under the heading on a narrow settings window.</summary>
    /// <param name="compact">Whether the viewport is at the small breakpoint.</param>
    internal void SetCompact(bool compact)
    {
        Grid.SetColumn(HeaderActions, compact ? 0 : 1);
        Grid.SetRow(HeaderActions, compact ? 1 : 0);
        HeaderActions.Margin = compact ? new Thickness(0, 8, 0, 0) : default;
    }

    /// <summary>Offers the kernel's protocols with the same answer role as this connection.</summary>
    /// <param name="catalog">The host protocol catalog.</param>
    /// <param name="language">The settings language.</param>
    internal void OfferProtocols(JsonArray catalog, InterfaceLanguage language)
    {
        if (!_allowRetype || _storedProtocol is not { } stored || ProtocolField.IsVisible)
        {
            return;
        }

        var wires = catalog.OfType<JsonObject>().ToArray();
        var current = wires.FirstOrDefault(wire => (string?)wire["kind"] == stored);
        if (current is null)
        {
            return;
        }

        var role = (string?)current["answers"] ?? "chat";
        var choices = wires.Where(wire => ((string?)wire["answers"] ?? "chat") == role)
            .Select(wire => (string?)wire["kind"])
            .OfType<string>().Distinct(StringComparer.Ordinal)
            .Select(protocol => new ComboBoxItem { Tag = protocol, Content = ProtocolName(protocol, language) })
            .ToArray();
        if (choices.Length > 1)
        {
            ProtocolDraft.ItemsSource = choices;
            ProtocolDraft.SelectedItem = choices.Single(item => Equals(item.Tag, stored));
            ProtocolField.IsVisible = true;
        }
    }

    /// <summary>Fills the sheet from the selected account and binds its actions.</summary>
    /// <param name="account">Grouped provider entries.</param>
    /// <param name="entry">Selected protocol entry.</param>
    /// <param name="language">The settings language.</param>
    /// <param name="api">Kernel provider commands.</param>
    /// <param name="token">Cancels the work with its settings host.</param>
    /// <param name="selectProtocol">Selects another configured protocol.</param>
    /// <param name="rename">Opens inline rename.</param>
    /// <param name="remove">Requests removal of the selected entry.</param>
    /// <param name="saved">Reads back an acknowledged write.</param>
    /// <param name="checkedProvider">Refreshes the account's status indicator.</param>
    /// <param name="describe">Formats kernel errors in the settings language.</param>
    internal void Show(
        ProviderServiceAccount account,
        JsonObject entry,
        InterfaceLanguage language,
        StudioPageClient api,
        CancellationToken token,
        Action<string> selectProtocol,
        Action rename,
        Action remove,
        Func<Task> saved,
        Action checkedProvider,
        Func<Exception, string> describe)
    {
        var kind = ProviderServiceAccount.Kind(entry);
        _storedProtocol = kind;
        _allowRetype = account.Entries.Count == 1;
        var name = ProviderServiceAccount.Name(entry);
        var models = entry["models"]?.AsArray().Select(model => model!.GetValue<string>()).ToArray() ?? [];
        ProviderTitle.Text = account.Label;
        ProviderSubtitle.Text = ProtocolName(kind, language) + " · " + account.Host;
        ToolTip.SetTip(ProviderSubtitle, ProviderSubtitle.Text);
        UseBadge.IsVisible = account.InUse;
        UseLabel.Text = Text("models.in_use");
        Probe.Content = Text("common.test_it");
        Remove.Content = Text("common.delete");
        var renameLabel = MessageCatalog.Get("models.rename_name_f2", language, ("name", account.Label));
        AutomationProperties.SetName(RenameAction, renameLabel);
        ToolTip.SetTip(RenameAction, renameLabel);
        RenameAction.Click += (_, _) => rename();
        Remove.Click += (_, _) => remove();

        Options.IsVisible = account.Entries.Count > 1;
        ProtocolLabel.Text = Text("common.protocol");
        var hasSearchAlternative = account.Entries.Values.Any(value => (bool?)value["canWebSearch"] == true);
        var protocolLacksSearch = (bool?)entry["canWebSearch"] != true;
        ProtocolHint.Text = Text(hasSearchAlternative && protocolLacksSearch ? "models.two_ways_in_to_the_2" : "models.two_ways_in_to_the");
        foreach (var (protocol, candidate) in account.Entries)
        {
            var chip = new Button { Tag = protocol, Classes = { "provider-kind" } };
            chip.Classes.Set("active", protocol == kind);
            AutomationProperties.SetAutomationId(chip, "action:provider.protocol");
            var caption = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            caption.Children.Add(new TextBlock { Text = ProtocolName(protocol, language), VerticalAlignment = VerticalAlignment.Center });
            if ((bool?)candidate["canWebSearch"] == true)
            {
                var perk = new TextBlock { Text = Text("models.web_search"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                perk.Bind(TextBlock.ForegroundProperty, perk.GetResourceObservable("NetInkBrush"));
                var badge = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 1), Child = perk };
                badge.Bind(Border.BackgroundProperty, badge.GetResourceObservable("NetWashBrush"));
                caption.Children.Add(badge);
            }

            chip.Content = caption;
            chip.Click += (_, _) => selectProtocol(protocol);
            Protocols.Children.Add(chip);
        }

        ProtocolFieldLabel.Text = Text("models.api_protocol");
        ProtocolFieldHint.Text = Text("models.follow_the_provider_documentation_a");
        AutomationProperties.SetName(ProtocolDraft, ProtocolFieldLabel.Text);
        EndpointLabel.Text = Text("models.endpoint_address");
        Endpoint.Text = (string?)entry["baseUrl"];
        KeyLabel.Text = Text("models.api_key_leave_blank_to");
        AutomationProperties.SetName(Endpoint, EndpointLabel.Text);
        AutomationProperties.SetName(ApiKey, KeyLabel.Text);
        LimitsTitle.Text = Text("models.model_limits");
        LimitsHint.Text = Text("models.use_the_model_documentation_instead");
        ContextLabel.Text = Text("common.context_window");
        ContextHint.Text = Text("models.the_total_input_tool_results");
        OutputLabel.Text = Text("models.maximum_output");
        OutputHint.Text = Text("models.the_per_turn_generation_limit");
        ContextLimit.Text = LimitText(entry["contextWindow"]);
        OutputLimit.Text = LimitText(entry["maxOutputTokens"]);
        ContextLimit.PlaceholderText = Text("common.not_declared");
        OutputLimit.PlaceholderText = Text("common.auto");
        AutomationProperties.SetName(ContextLimit, ContextLabel.Text);
        AutomationProperties.SetName(OutputLimit, OutputLabel.Text);
        ModelsLabel.Text = Text("common.models");
        ModelNames.Text = string.Join(" · ", models);
        FillModelLimits(models, entry, language);
        Save.Content = Text("common.save");
        Revert.Content = Text("common.revert");

        var editableFields = new[] { Endpoint, ApiKey, ContextLimit, OutputLimit }
            .Concat(_limits.Values.SelectMany(pair => new[] { pair.Window, pair.Output })).ToArray();
        var initial = editableFields.Select(field => field.Text ?? string.Empty).ToArray();
        var storedSearch = (bool?)entry["webSearch"] == true;
        var search = storedSearch;
        var canSearch = (bool?)entry["canWebSearch"] == true;
        WebSearchLabel.Text = Text("models.web_search");
        WebSearchHint.Text = Text("models.the_endpoint_runs_the_search");
        EnableSearch.Content = Text("models.on");
        DisableSearch.Content = Text("common.off");
        EnableSearch.Click += (_, _) =>
        {
            search = true;
            RefreshFooter();
        };
        DisableSearch.Click += (_, _) =>
        {
            search = false;
            RefreshFooter();
        };
        ProtocolDraft.SelectionChanged += (_, _) => RefreshFooter();
        string[] Values() => editableFields.Select(field => field.Text ?? string.Empty).ToArray();
        string CurrentProtocol() => (ProtocolDraft.SelectedItem as ComboBoxItem)?.Tag as string ?? kind;

        void RefreshFooter()
        {
            var protocol = CurrentProtocol();
            WebSearchPanel.IsVisible = canSearch && protocol == kind;
            EnableSearch.Classes.Set("active", search);
            DisableSearch.Classes.Set("active", !search);
            HasChanges = protocol != kind || (canSearch && search != storedSearch) || !initial.SequenceEqual(Values(), StringComparer.Ordinal);
            Revert.IsEnabled = HasChanges;
            Save.IsEnabled = HasChanges && editableFields.Skip(2).All(field => string.IsNullOrEmpty(field.Text)
                || int.TryParse(field.Text, NumberStyles.None, CultureInfo.InvariantCulture, out _));
            SaveStatus.Text = Text(HasChanges ? "models.unsaved_changes" : "models.no_changes");
            SaveDot.Classes.Set("dirty", HasChanges);
        }

        RefreshFooter();
        foreach (var field in editableFields)
        {
            field.TextChanged += (_, _) => RefreshFooter();
        }

        Probe.Click += async (_, _) =>
        {
            HeaderActions.IsEnabled = false;
            Options.IsEnabled = false;
            Probe.Content = Text("models.testing_2");
            CheckNotice.IsVisible = false;
            try
            {
                var result = await api.CheckProviderAsync(name, token).ConfigureAwait(true);
                var success = (bool?)result["ok"] == true;
                entry["lastCheck"] = result.DeepClone();
                CheckTitle.Text = success
                    ? Text("common.connected") + " · " + ProtocolName((string?)result["kind"] ?? kind, language) + " · "
                        + MessageCatalog.Get("models.n_models", language, ("n", (result["models"]?.AsArray().Count ?? 0).ToString(CultureInfo.CurrentCulture)))
                    : Text("models.unreachable");
                var foundKind = (string?)result["kind"] ?? kind;
                CheckBody.Text = success
                    ? (bool?)result["matches"] == false
                        ? MessageCatalog.Get("models.recorded_as_had_but_it", language, ("had", ProtocolName(kind, language)), ("got", ProtocolName(foundKind, language)))
                        : Text("models.the_key_works_and_the")
                    : CheckFailure(result, language);
                CheckNotice.Classes.Set("check-ok", success);
                checkedProvider();
            }
            catch (Exception error) when (!token.IsCancellationRequested)
            {
                CheckTitle.Text = Text("models.unreachable");
                CheckBody.Text = describe(error);
                CheckNotice.Classes.Remove("check-ok");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            finally
            {
                Probe.Content = Text("common.test_it");
                HeaderActions.IsEnabled = Options.IsEnabled = true;
            }

            CheckNotice.IsVisible = true;
        };
        Revert.Click += (_, _) =>
        {
            for (var i = 0; i < editableFields.Length; i++)
            {
                editableFields[i].Text = initial[i];
            }

            search = storedSearch;
            if (ProtocolField.IsVisible)
            {
                ProtocolDraft.SelectedItem = ProtocolDraft.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, kind));
            }

            RefreshFooter();
        };
        Save.Click += async (_, _) =>
        {
            IsEnabled = false;
            SaveStatus.Text = Text("common.saving_2");
            try
            {
                var body = new JsonObject
                {
                    ["name"] = name,
                    ["kind"] = CurrentProtocol(),
                    ["baseUrl"] = Endpoint.Text?.Trim() ?? string.Empty,
                    ["apiKey"] = ApiKey.Text?.Trim() ?? string.Empty,
                    ["models"] = new JsonArray(models.Select(model => (JsonNode?)JsonValue.Create(model)).ToArray()),
                    ["default"] = (string?)entry["default"] ?? models.FirstOrDefault() ?? string.Empty,
                    ["vision"] = entry["visionModels"]?.DeepClone() ?? new JsonArray(),
                    ["contextWindow"] = ParseLimit(ContextLimit.Text),
                    ["maxOutputTokens"] = ParseLimit(OutputLimit.Text),
                };
                if (canSearch && CurrentProtocol() == kind && search != storedSearch)
                {
                    body["webSearch"] = search;
                }

                if (_limits.Count > 0)
                {
                    var overrides = new JsonObject();
                    foreach (var (model, (window, output)) in _limits)
                    {
                        var context = ParseLimit(window.Text);
                        var maximum = ParseLimit(output.Text);
                        if (context > 0 || maximum > 0)
                        {
                            overrides[model] = new JsonObject { ["contextWindow"] = context, ["maxOutputTokens"] = maximum };
                        }
                    }

                    body["modelLimits"] = overrides;
                }

                await api.EditProviderAsync(body, token).ConfigureAwait(true);
                ApiKey.Text = string.Empty;
                initial = Values();
                RefreshFooter();
                await saved().ConfigureAwait(true);
            }
            catch (Exception error) when (!token.IsCancellationRequested)
            {
                SaveStatus.Text = describe(error);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            finally
            {
                IsEnabled = true;
            }
        };

        string Text(string key) => MessageCatalog.Get(key, language);
    }

    /// <summary>Formats the connection's returned refusal and endpoint response.</summary>
    /// <param name="result">Probe result from the kernel.</param>
    /// <param name="language">The settings language.</param>
    /// <returns>The localized refusal with the HTTP response, when present.</returns>
    private static string CheckFailure(JsonObject result, InterfaceLanguage language)
    {
        var reason = result.Deserialize<Reason>();
        if (reason is not null && (string?)result["target"] is { } target)
        {
            var values = reason.Params?.ToDictionary() ?? [];
            values["target"] = JsonSerializer.SerializeToElement(target);
            reason = reason with
            {
                Params = values,
            };
        }

        var explanation = string.IsNullOrEmpty(reason?.Code)
            ? MessageCatalog.Get("models.the_check_failed_and_gave", language)
            : RefusalText.Say(reason, null, null, language);
        var status = (int?)result["httpStatus"] is > 0 and int code ? $"HTTP {code}" : null;
        return string.Join(" · ", new[] { explanation, status, (string?)result["detail"] }.Where(part => !string.IsNullOrEmpty(part)));
    }

    /// <summary>Uses a blank field for an inherited limit.</summary>
    /// <param name="value">Stored numeric setting.</param>
    /// <returns>The editable representation.</returns>
    private static string LimitText(JsonNode? value) => (int?)value is > 0 and int n ? n.ToString(CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>Reads a nonnegative token limit with zero denoting inheritance.</summary>
    /// <param name="text">Field text.</param>
    /// <returns>The parsed limit.</returns>
    private static int ParseLimit(string? text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;

    /// <summary>Adds one pair of controls for every independently configurable model limit.</summary>
    /// <param name="models">Models on the selected protocol.</param>
    /// <param name="entry">Stored values and effective inherited limits.</param>
    /// <param name="language">The settings language.</param>
    private void FillModelLimits(string[] models, JsonObject entry, InterfaceLanguage language)
    {
        if (models.Length < 2 && entry["modelLimits"] is not JsonObject { Count: > 0 })
        {
            return;
        }

        var title = new WrapPanel { ItemSpacing = 10, LineSpacing = 3 };
        title.Children.Add(new TextBlock { Text = MessageCatalog.Get("models.limits_per_model", language), FontWeight = FontWeight.SemiBold, FontSize = 13 });
        title.Children.Add(new TextBlock { Text = MessageCatalog.Get("models.a_blank_field_follows_the", language), Classes = { "provider-caption" } });
        PerModel.Children.Add(title);
        foreach (var model in models)
        {
            var data = entry["modelLimits"]?[model];
            var inherited = entry["inheritedLimits"]?[model];
            var heading = new TextBlock { Text = model, FontSize = 13, FontWeight = FontWeight.Bold };
            heading.Bind(TextBlock.FontFamilyProperty, heading.GetResourceObservable("WorkbenchMonoFamily"));
            var pair = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 10 };
            var window = AddLimit("contextWindow", "models.inherits_not_declared", "models.context_window_for_model", "common.window", 0);
            var output = AddLimit("maxOutputTokens", "models.inherits_automatic", "models.max_output_for_model", "common.output", 1);
            _limits.Add(model, (window, output));
            var section = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 7, 0, 0) };
            section.Bind(Border.BorderBrushProperty, section.GetResourceObservable("HairBrush"));
            section.Child = new StackPanel { Children = { heading, pair }, Spacing = 5 };
            PerModel.Children.Add(section);

            TextBox AddLimit(string key, string fallback, string label, string unit, int column)
            {
                var input = new TextBox
                {
                    Text = LimitText(data?[key]),
                    TextAlignment = TextAlignment.Right,
                    Padding = new Thickness(10, 0, 47, 0),
                    PlaceholderText = (int?)inherited?[key] is > 0 and int inheritedLimit
                        ? MessageCatalog.Get("models.inherits_n", language, ("n", inheritedLimit.ToString("N0", CultureInfo.InvariantCulture)))
                        : MessageCatalog.Get(fallback, language),
                    Classes = { "provider-field" },
                };
                AutomationProperties.SetName(input, MessageCatalog.Get(label, language, ("model", model)));
                var suffix = new TextBlock
                {
                    Text = MessageCatalog.Get(unit, language),
                    Classes = { "provider-caption" },
                    IsHitTestVisible = false,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0),
                };
                var field = new Panel { Children = { input, suffix } };
                Grid.SetColumn(field, column);
                pair.Children.Add(field);
                return input;
            }
        }
    }
}
