using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>The model settings page's provider list and selected provider sheet.</summary>
public sealed partial class ModelServices : UserControl
{
    /// <summary>Providers the page last read.</summary>
    private IReadOnlyList<JsonObject> _providers = [];

    /// <summary>Currently selected provider name.</summary>
    private string? _selected;

    /// <summary>Language labels are drawn in.</summary>
    private InterfaceLanguage _language = InterfaceLanguage.Zh;

    /// <summary>Client used for the probe action.</summary>
    private StudioPageClient? _api;

    /// <summary>Saved provider ordering.</summary>
    private IReadOnlyList<string> _providerOrder = [];

    /// <summary>Cancellation for this page.</summary>
    private CancellationToken _token;

    /// <summary>Initializes a new instance of the <see cref="ModelServices"/> class.</summary>
    public ModelServices()
    {
        InitializeComponent();
        RailTitle.Text = MessageCatalog.Get("common.model_services", InterfaceLanguage.Zh);
        Add.Content = MessageCatalog.Get("models.add_model_service", InterfaceLanguage.Zh);
        Add.Click += (_, _) => AddRequested?.Invoke();
    }

    /// <summary>Raised when the reader wants to add another service.</summary>
    public event Action? AddRequested;

    /// <summary>Raised when the reader moves the selected provider.</summary>
    public event Action<string, int>? ProviderMoved;

    /// <summary>Raised when the reader asks to remove the selected provider.</summary>
    public event Action<string>? DeleteRequested;

    /// <summary>Projects the provider inventory and keeps the chosen row open.</summary>
    /// <param name="providers">Providers read from the kernel.</param>
    /// <param name="language">Language for the labels.</param>
    /// <param name="api">Client for the probe action.</param>
    /// <param name="token">Cancels the page.</param>
    /// <param name="providerOrder">Saved provider ordering.</param>
    public void Show(
        JsonArray providers,
        InterfaceLanguage language,
        StudioPageClient api,
        CancellationToken token,
        IReadOnlyList<string>? providerOrder = null)
    {
        _providerOrder = providerOrder ?? [];
        var rank = _providerOrder.Select((name, index) => (Name: name, Index: index))
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.Ordinal);
        _providers = providers.OfType<JsonObject>()
            .OrderBy(item => rank.TryGetValue(ProviderName(item), out var at) ? at : int.MaxValue)
            .ToArray();
        _language = language;
        _api = api;
        _token = token;
        RailTitle.Text = MessageCatalog.Get("common.model_services", language);
        Add.Content = MessageCatalog.Get("models.add_model_service", language);
        _selected = _providers.FirstOrDefault(provider => (bool?)provider["inUse"] == true) is { } current
            ? ProviderName(current)
            : _providers.FirstOrDefault() is { } first ? ProviderName(first) : null;
        DrawList();
        DrawDetail();
    }

    /// <summary>Draws the provider rail.</summary>
    private void DrawList()
    {
        ProviderList.Children.Clear();
        foreach (var provider in _providers)
        {
            var name = ProviderName(provider);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*,Auto"), ColumnSpacing = 8 };
            var mark = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.Parse("#4D6BFE"), 0.16),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = (string?)provider["mark"] is { Length: > 0 } glyph ? glyph[..1].ToUpperInvariant() : name[..1].ToUpperInvariant(),
                    FontSize = 10,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(Color.Parse("#4D6BFE")),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            grid.Children.Add(mark);
            var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Label(provider, "displayName", name, "provider-name"));
            text.Children.Add(Label(provider, "baseUrl", string.Empty, "provider-host"));
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            if ((bool?)provider["inUse"] == true)
            {
                actions.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#4D6BFE"), 0.14),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(5, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = MessageCatalog.Get("models.in_use", _language),
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Color.Parse("#4D6BFE")),
                    },
                });
            }

            if (name == _selected)
            {
                foreach (var (arrow, direction) in new[] { ("\u2191", -1), ("\u2193", 1) })
                {
                    var move = new Button { Content = arrow, Width = 20, Height = 20, MinWidth = 0, MinHeight = 0, Padding = default, FontSize = 10 };
                    move.Classes.Add("provider-action");
                    move.Click += (_, _) => ProviderMoved?.Invoke(name, direction);
                    actions.Children.Add(move);
                }
            }

            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);

            var button = new Button { Content = grid, Tag = name };
            button.Classes.Add("provider-item");
            button.Classes.Set("active", name == _selected);
            AutomationProperties.SetName(button, name);
            button.Click += (_, _) =>
            {
                _selected = name;
                DrawList();
                DrawDetail();
            };
            ProviderList.Children.Add(button);
        }
    }

    /// <summary>Draws the selected provider's details.</summary>
    private void DrawDetail()
    {
        Detail.Children.Clear();
        var provider = _providers.FirstOrDefault(item => ProviderName(item) == _selected);
        if (provider is null)
        {
            Detail.Children.Add(Label("models.add_a_model_service_to", "provider-value"));
            return;
        }

        var name = ProviderName(provider);
        var kind = (string?)provider["kind"] ?? "openai";
        var models = (provider["models"] as JsonArray)?.Select(item => item?.ToString()).Where(item => !string.IsNullOrEmpty(item)).ToArray() ?? [];
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        var titles = new StackPanel { Spacing = 3 };
        titles.Children.Add(Label(provider, "displayName", name, "provider-detail-name"));
        var protocol = kind == "anthropic" ? "Anthropic" : "OpenAI";
        titles.Children.Add(new TextBlock
        {
            Text = protocol + " 兼容 · " + ((string?)provider["baseUrl"] ?? string.Empty),
            Classes = { "provider-detail-kind" },
        });
        head.Children.Add(titles);
        var headActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if ((bool?)provider["inUse"] == true)
        {
            headActions.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.Parse("#4D6BFE"), 0.14),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2),
                Child = new TextBlock
                {
                    Text = MessageCatalog.Get("models.in_use", _language),
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.Parse("#4D6BFE")),
                },
            });
        }

        var probe = new Button { Content = MessageCatalog.Get("models.test_saved_settings", _language) };
        probe.Classes.Add("provider-action");
        probe.Click += async (_, _) => await ProbeAsync(name, probe).ConfigureAwait(true);
        headActions.Children.Add(probe);
        var delete = new Button { Content = MessageCatalog.Get("common.delete", _language) };
        delete.Classes.Add("provider-action");
        delete.Click += (_, _) => DeleteRequested?.Invoke(name);
        headActions.Children.Add(delete);
        Grid.SetColumn(headActions, 1);
        head.Children.Add(headActions);
        Detail.Children.Add(head);

        var kindRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Margin = new Thickness(0, 12, 0, 0) };
        var openai = new Button { Content = "OpenAI 兼容" };
        var anthropic = new Button { Content = "Anthropic 兼容" };
        var web = new Button { Content = MessageCatalog.Get("models.web_search", _language) };
        var webSearch = (bool?)provider["webSearch"] == true;
        foreach (var chip in new[] { openai, anthropic, web })
        {
            chip.Classes.Add("provider-kind");
            kindRow.Children.Add(chip);
        }

        openai.Classes.Set("active", kind == "openai");
        anthropic.Classes.Set("active", kind == "anthropic");
        web.Classes.Set("active", webSearch);
        openai.Click += (_, _) => SelectKind("openai", openai, anthropic);
        anthropic.Click += (_, _) => SelectKind("anthropic", openai, anthropic);
        web.Click += (_, _) =>
        {
            webSearch = !webSearch;
            web.Classes.Set("active", webSearch);
        };
        Detail.Children.Add(kindRow);

        var baseUrl = new TextBox { Text = (string?)provider["baseUrl"] ?? string.Empty };
        baseUrl.Classes.Add("provider-field");
        Detail.Children.Add(Label("models.test_target", "provider-label"));
        Detail.Children.Add(baseUrl);

        var apiKey = new TextBox { PasswordChar = '●', PlaceholderText = MessageCatalog.Get("models.api_key_leave_blank_to", _language) };
        apiKey.Classes.Add("provider-field");
        Detail.Children.Add(Label("models.api_key_leave_blank_to", "provider-label"));
        Detail.Children.Add(apiKey);

        var limits = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 10 };
        var context = new TextBox { Text = ((int?)provider["contextWindow"] ?? 0).ToString(System.Globalization.CultureInfo.CurrentCulture) };
        var output = new TextBox { Text = ((int?)provider["maxOutputTokens"] ?? 0).ToString(System.Globalization.CultureInfo.CurrentCulture) };
        foreach (var field in new[] { context, output })
        {
            field.Classes.Add("provider-field");
        }

        var contextPane = new StackPanel { Spacing = 4 };
        contextPane.Children.Add(Label("common.context_window", "provider-label"));
        contextPane.Children.Add(context);
        var outputPane = new StackPanel { Spacing = 4 };
        outputPane.Children.Add(Label("models.maximum_output", "provider-label"));
        outputPane.Children.Add(output);
        Grid.SetColumn(outputPane, 1);
        limits.Children.Add(contextPane);
        limits.Children.Add(outputPane);
        Detail.Children.Add(limits);

        Detail.Children.Add(Label("common.models", "provider-label"));
        Detail.Children.Add(Value(string.Join(" · ", models)));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 18, 0, 0) };
        var save = new Button { Content = MessageCatalog.Get("common.save", _language) };
        save.Classes.Add("provider-primary");
        var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 10, Foreground = new SolidColorBrush(Color.Parse("#5E8C61")) };
        save.Click += async (_, _) =>
        {
            if (_api is null)
            {
                return;
            }

            save.IsEnabled = false;
            status.Text = MessageCatalog.Get("common.saving_2", _language);
            try
            {
                var body = new JsonObject
                {
                    ["name"] = name,
                    ["kind"] = kind,
                    ["baseUrl"] = baseUrl.Text?.Trim() ?? string.Empty,
                    ["apiKey"] = apiKey.Text ?? string.Empty,
                    ["models"] = new JsonArray(models.Select(model => (JsonNode?)JsonValue.Create(model)).ToArray()),
                    ["default"] = (string?)provider["default"] ?? models.FirstOrDefault() ?? string.Empty,
                    ["contextWindow"] = ParseLimit(context.Text),
                    ["maxOutputTokens"] = ParseLimit(output.Text),
                    ["webSearch"] = webSearch,
                };
                await _api.EditProviderAsync(body, _token).ConfigureAwait(true);
                status.Text = MessageCatalog.Get("common.saved_not_applied_yet", _language);
            }
            catch (Exception error)
            {
                status.Text = error.Message;
            }
            finally
            {
                save.IsEnabled = true;
            }
        };
        actions.Children.Add(save);
        var revert = new Button { Content = MessageCatalog.Get("common.revert", _language) };
        revert.Classes.Add("provider-action");
        revert.Click += (_, _) => DrawDetail();
        actions.Children.Add(revert);
        actions.Children.Add(status);
        Detail.Children.Add(actions);

        void SelectKind(string selected, Button openAiButton, Button anthropicButton)
        {
            kind = selected;
            openAiButton.Classes.Set("active", selected == "openai");
            anthropicButton.Classes.Set("active", selected == "anthropic");
        }
    }

    /// <summary>Parses a token limit field, treating an invalid value as zero.</summary>
    /// <param name="text">Field text.</param>
    /// <returns>The parsed limit.</returns>
    private int ParseLimit(string? text) =>
        int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.CurrentCulture, out var value)
            ? Math.Max(0, value)
            : 0;

    /// <summary>Probes the selected provider and leaves the result in the sheet.</summary>
    /// <param name="name">Provider name.</param>
    /// <param name="button">Probe control to disable while waiting.</param>
    /// <returns>The probe.</returns>
    private async Task ProbeAsync(string name, Button button)
    {
        if (_api is null)
        {
            return;
        }

        button.IsEnabled = false;
        try
        {
            await _api.CheckProviderAsync(name, _token).ConfigureAwait(true);
            button.Content = MessageCatalog.Get("models.connected", _language);
        }
        catch (Exception error)
        {
            button.Content = MessageCatalog.Get("models.connection_failed", _language);
            ToolTip.SetTip(button, error.Message);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    /// <summary>Reads the display name, falling back to the configured name.</summary>
    /// <param name="provider">Provider record.</param>
    /// <returns>The label.</returns>
    private string ProviderName(JsonObject provider) =>
        (string?)provider["displayName"] is { Length: > 0 } display
            ? display
            : (string?)provider["name"] ?? string.Empty;

    /// <summary>Builds one text label from a provider field.</summary>
    /// <param name="provider">Provider record.</param>
    /// <param name="field">Field to read.</param>
    /// <param name="fallback">Value when the field is empty.</param>
    /// <param name="classes">Style classes.</param>
    /// <returns>The label.</returns>
    private TextBlock Label(JsonObject provider, string field, string fallback, string classes)
    {
        var text = new TextBlock
        {
            Text = (string?)provider[field] is { Length: > 0 } value ? value : fallback,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.Classes.Add(classes);
        return text;
    }

    /// <summary>Builds one translated label.</summary>
    /// <param name="key">Message key.</param>
    /// <param name="classes">Style classes.</param>
    /// <returns>The label.</returns>
    private TextBlock Label(string key, string classes)
    {
        var text = new TextBlock { Text = MessageCatalog.Get(key, _language) };
        text.Classes.Add(classes);
        return text;
    }

    /// <summary>Builds a value line.</summary>
    /// <param name="value">Visible value.</param>
    /// <returns>The line.</returns>
    private TextBlock Value(string value)
    {
        var text = new TextBlock { Text = value };
        text.Classes.Add("provider-value");
        return text;
    }
}
