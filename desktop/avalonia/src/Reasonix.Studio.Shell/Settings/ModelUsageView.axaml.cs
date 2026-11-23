using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>The settings table that maps each use to its model and reasoning choices.</summary>
internal sealed partial class ModelUsageView : UserControl
{
    /// <summary>The model roles in reference display order.</summary>
    private static readonly (string Id, string Title, string Hint)[] s_roles =
    [
        ("planner", "common.plan", "settings.drafts_a_plan_read_only"),
        ("subagent", "common.subagents", "usage.work_sent_out"),
        ("vision", "models.vision", "usage.reads_images_the_main_model"),
        ("guardian", "models.review", "usage.audits_this_turn_independently"),
        ("title", "session.auto_name", "usage.generate_short_conversation_titles"),
        ("decision", "models.decision", "usage.the_backend_system_one_asks"),
    ];

    /// <summary>Catalog, including hosts that answer decision requests.</summary>
    private ModelChoice[] _models = [];

    /// <summary>The persisted assignments returned by the kernel.</summary>
    private JsonObject? _roles;

    /// <summary>Profile and project overrides read from the live host.</summary>
    private JsonObject? _overrides;

    /// <summary>Active session identity and reasoning level.</summary>
    private SessionStatus? _status;

    /// <summary>Reuses the existing service accounts and their protocol choice.</summary>
    private ModelServices? _services;

    /// <summary>The settings client for this runtime.</summary>
    private StudioPageClient? _api;

    /// <summary>Reads the current session without changing the selection.</summary>
    private Func<Task<SessionStatus?>>? _readStatus;

    /// <summary>Selects the current and future model through the correct owners.</summary>
    private Func<string, Task>? _pickMain;

    /// <summary>Formats refusals in the window language.</summary>
    private Func<Exception, string>? _describe;

    /// <summary>The settings language.</summary>
    private InterfaceLanguage _language;

    /// <summary>Runtime request lifetime.</summary>
    private CancellationToken _token;

    /// <summary>The outer viewport supplying the old CSS breakpoint.</summary>
    private Window? _window;

    /// <summary>Whether the table is in its stacked configuration.</summary>
    private bool _compact;

    /// <summary>A pending write or reread owning the role pickers.</summary>
    private bool _busy;

    /// <summary>Initializes a new instance of the <see cref="ModelUsageView"/> class.</summary>
    public ModelUsageView() => InitializeComponent();

    /// <summary>Reads the catalog and the settings owned by the runtime.</summary>
    /// <param name="api">Runtime settings client.</param>
    /// <param name="services">Services on the same settings page.</param>
    /// <param name="readStatus">Reads the active runtime model and effort.</param>
    /// <param name="pickMain">Chooses the current and persisted default model.</param>
    /// <param name="language">The settings language.</param>
    /// <param name="describe">Refusal formatter.</param>
    /// <param name="token">The settings owner's lifetime.</param>
    /// <returns>The initial render data.</returns>
    internal async Task OpenAsync(
        StudioPageClient api,
        ModelServices services,
        Func<Task<SessionStatus?>> readStatus,
        Func<string, Task> pickMain,
        InterfaceLanguage language,
        Func<Exception, string> describe,
        CancellationToken token)
    {
        _api = api;
        _services = services;
        _readStatus = readStatus;
        _pickMain = pickMain;
        _language = language;
        _describe = describe;
        _token = token;
        UseHeading.Text = Text("usage.use");
        ModelHeading.Text = Text("usage.model");
        EffortHeading.Text = Text("usage.effort");
        ConnectionHeading.Text = Text("usage.connections");
        services.UsageChanged += Refresh;
        await ReadAsync().ConfigureAwait(true);
        Draw();
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            _window = window;
            window.SizeChanged += Resize;
            SetWidth(window.ClientSize.Width);
        }
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window is not null)
        {
            _window.SizeChanged -= Resize;
            _window = null;
        }

        if (_services is not null)
        {
            _services.UsageChanged -= Refresh;
        }

        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Formats the model's label without hiding its wire reference.</summary>
    /// <param name="model">Catalog entry.</param>
    /// <returns>Display name.</returns>
    private static string Shown(ModelChoice model) => string.IsNullOrWhiteSpace(model.ModelAlias) ? model.ModelShort ?? model.Model : model.ModelAlias.Trim();

    /// <summary>Controls which source types a role may offer.</summary>
    /// <param name="model">Catalog entry.</param>
    /// <param name="role">Purpose.</param>
    /// <returns>Whether the source belongs to this selector.</returns>
    private static bool Matches(ModelChoice model, string role) => role switch
    {
        "decision" => model.Answers == "decision",
        "vision" => model.Answers != "decision" && model.Vision,
        "web_search" => model.WebSearch && !model.NotAdded,
        _ => model.Answers != "decision",
    };

    /// <summary>Reads an independent part of the settings without hiding working selectors when that part fails.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="read">Kernel read already in progress.</param>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The result when available.</returns>
    private static async Task<T?> OptionalReadAsync<T>(Task<T?> read, CancellationToken token)
        where T : class
    {
        try
        {
            return await read.ConfigureAwait(false);
        }
        catch (Exception error) when (!token.IsCancellationRequested && error is KernelRefusal or System.Net.Http.HttpRequestException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Maps the settings viewport to the same wide or narrow table as the reference.</summary>
    /// <param name="sender">Owning window.</param>
    /// <param name="e">Updated size.</param>
    private void Resize(object? sender, SizeChangedEventArgs e) => SetWidth(_window?.ClientSize.Width ?? e.NewSize.Width);

    /// <summary>Applies the model table breakpoint.</summary>
    /// <param name="width">Viewport width.</param>
    private void SetWidth(double width)
    {
        _compact = width <= 860;
        UsageHeader.IsVisible = !_compact && _models.Length > 0;
        foreach (var row in UsageRows.Children.OfType<ModelUseRow>())
        {
            row.SetCompact(_compact);
        }
    }

    /// <summary>Reads the current catalog, configured roles, and status together.</summary>
    /// <returns>The authoritative snapshot.</returns>
    private async Task ReadAsync()
    {
        var api = _api!;
        var models = api.SettingsModelsAsync(_token);
        var roles = OptionalReadAsync(api.ModelRolesAsync(_token), _token);
        var overrides = OptionalReadAsync(api.ModelRoleOverridesAsync(_token), _token);
        var status = OptionalReadAsync(_readStatus!(), _token);
        await Task.WhenAll(models, roles, overrides, status).ConfigureAwait(true);
        _models = await models.ConfigureAwait(true);
        _roles = await roles.ConfigureAwait(true);
        _overrides = await overrides.ConfigureAwait(true);
        _status = await status.ConfigureAwait(true);
    }

    /// <summary>Responds to provider ordering, protocol selection and inventory changes.</summary>
    private async void Refresh()
    {
        if (!_busy)
        {
            await CommitAsync(() => Task.CompletedTask).ConfigureAwait(true);
        }
    }

    /// <summary>Builds the native rows from the same persisted and effective values as the old page.</summary>
    private void Draw()
    {
        UsageRows.Children.Clear();
        UsageHeader.IsVisible = !_compact && _models.Length > 0;
        VisionNote.IsVisible = _models.Length > 0;
        UsageMissing.IsVisible = _models.Length == 0 || _roles is null;
        UsageMissing.Text = Text(_models.Length == 0 ? "usage.could_not_read_the_model" : "usage.could_not_read_the_role");
        if (_models.Length == 0)
        {
            return;
        }

        var main = _status?.ModelRef is { Length: > 0 } active ? active : _models.FirstOrDefault(model => model.Active)?.Ref ?? string.Empty;
        var picked = _models.FirstOrDefault(model => model.Default)?.Ref ?? main;
        var groups = Accounts();
        var root = Row("default", "common.default_model", "usage.this_conversation_and_most_tasks", Service(groups, picked), Traits(ByRef(picked)), false);
        root.SetModels(Choices(groups, "default", main, picked, null), picked, true, model => CommitAsync(() => _pickMain!(model)));
        root.SetEfforts(ByRef(main)?.Efforts, _status?.Effort ?? "auto", true, false, _language, level => CommitAsync(() => _api!.SetEffortAsync(level, _token)));
        Add(root);

        if (_roles is not null)
        {
            foreach (var (id, title, hint) in s_roles)
            {
                var reference = (string?)_roles["roles"]?[id] ?? string.Empty;
                var roleModel = ByRef(reference.Length == 0 && id is not ("title" or "decision") ? main : reference);
                var none = id == "decision" && !_models.Any(model => Matches(model, id));
                var empty = Text(id switch
                {
                    "title" => "usage.do_not_use",
                    "decision" when none => "usage.no_source_yet",
                    "decision" => "usage.do_not_use",
                    _ => "usage.follow_the_main_model",
                });
                var follows = reference.Length == 0 && id is not ("title" or "decision");
                var fallbackConnection = id switch
                {
                    "title" => Text("usage.use_message_preview"),
                    "decision" when none => Text("usage.add_a_decision_source_under"),
                    "decision" => string.Empty,
                    _ => Text("usage.follows_the_default"),
                };
                var connection = reference.Length > 0 ? Service(groups, reference) : fallbackConnection;
                var row = Row(id, title, hint, connection, string.Empty, follows);
                row.SetModels(Choices(groups, id, main, reference, empty), reference, !none, next => ChangeRoleAsync(id, next, "auto"));
                row.SetEfforts(
                    id == "title" ? null : roleModel?.Efforts,
                    (string?)_roles["efforts"]?[id] ?? "auto",
                    id is not ("title" or "decision"),
                    false,
                    _language,
                    level => ChangeRoleAsync(id, reference.Length == 0 ? main : reference, level));
                DrawOverrides(row, id);
                Add(row);
            }

            AddSearch(groups, main);
        }

        var visionRef = (string?)_roles?["roles"]?["vision"];
        var helperRef = (string?)_roles?["roles"]?["subagent"];
        var vision = ByRef(!string.IsNullOrEmpty(visionRef) ? visionRef : !string.IsNullOrEmpty(helperRef) ? helperRef : main);
        VisionNote.Text = vision?.Vision == true
            ? MessageCatalog.Get("usage.images_the_main_model_cannot_2", _language, ("model", vision.Model))
            : Text("usage.images_the_main_model_cannot");
    }

    /// <summary>Creates a row with localized headings.</summary>
    /// <param name="id">Role identity.</param>
    /// <param name="title">Localized purpose key.</param>
    /// <param name="hint">Localized descriptive key.</param>
    /// <param name="connection">Effective service.</param>
    /// <param name="traits">Optional extra source information.</param>
    /// <param name="follows">Whether the role inherits its source.</param>
    /// <returns>The configured row.</returns>
    private ModelUseRow Row(string id, string title, string hint, string connection, string traits, bool follows)
    {
        var row = new ModelUseRow();
        row.Present(id, Text(title), Text(hint), connection, traits, follows, _language);
        return row;
    }

    /// <summary>Places a row in the current responsive table.</summary>
    /// <param name="row">The row to add.</param>
    private void Add(ModelUseRow row)
    {
        row.SetCompact(_compact);
        UsageRows.Children.Add(row);
    }

    /// <summary>Builds the search-model row, including project overrides and unavailable sources.</summary>
    /// <param name="groups">Available service groups.</param>
    /// <param name="main">Current model reference.</param>
    private void AddSearch(UsageAccount[] groups, string main)
    {
        var value = (string?)_roles?["web_search"] ?? string.Empty;
        var reference = value.Equals("auto", StringComparison.OrdinalIgnoreCase) ? string.Empty : value;
        var effective = (string?)_roles?["web_search_effective"] ?? string.Empty;
        var reason = (string?)_roles?["web_search_reason"] switch
        {
            "bad_ref" => "usage.the_search_model_must_be",
            "not_added" => "usage.the_search_models_connection_has",
            "model_removed" => "usage.the_search_model_is_no",
            "unsupported" => "usage.the_search_models_protocol_has",
            "no_credentials" => "usage.the_search_models_connection_has_2",
            _ => null,
        };
        var automatic = effective.Length > 0
            ? MessageCatalog.Get("usage.automatic_the_conversation_models_built", _language, ("model", Service(groups, effective)))
            : Text("usage.the_conversation_model_has_no");
        var configured = reason is null ? Service(groups, reference) : Text(reason);
        var used = reference.Length > 0 ? configured : automatic;
        var notes = new List<string>();
        if (!_models.Any(model => model.WebSearch))
        {
            notes.Add(Text("usage.no_search_models_available"));
        }

        if ((string?)_roles?["web_search_source"] == "project")
        {
            notes.Add(effective.Length > 0
                ? MessageCatalog.Get("usage.project_override_model_is_used", _language, ("model", Service(groups, effective)))
                : Text("usage.the_project_config_overrides_this"));
        }

        var row = Row("web_search", "common.web_search", "usage.model_for_independent_search_requests", used, string.Join("\n", notes), false);
        row.SetModels(Choices(groups, "web_search", main, reference, Text("common.pick_one_automatically")), reference, true, next => ChangeRoleAsync("web_search", next, null));
        row.SetEfforts(null, "auto", false, true, _language, _ => Task.CompletedTask);
        Add(row);
    }

    /// <summary>Shows explicit profile precedence and the user's clear action.</summary>
    /// <param name="row">The role row.</param>
    /// <param name="role">Role identifier.</param>
    private void DrawOverrides(ModelUseRow row, string role)
    {
        if (_overrides?[role] is not JsonArray { Count: > 0 } items)
        {
            return;
        }

        row.OverridePanel.IsVisible = true;
        foreach (var item in items.OfType<JsonObject>())
        {
            var key = (string?)item["key"] ?? string.Empty;
            var model = (string?)item["model"] ?? string.Empty;
            row.OverridePanel.Children.Add(new TextBlock
            {
                Text = MessageCatalog.Get("usage.key_is_pinned_to_model", _language, ("key", key), ("model", model)),
                Classes = { "usage-caption" },
                VerticalAlignment = VerticalAlignment.Center,
            });
            if ((string?)item["scope"] == "user")
            {
                var clear = new Button { Content = Text("usage.follow_this_choice"), FontSize = 11, Padding = new Thickness(8, 2) };
                Avalonia.Automation.AutomationProperties.SetAutomationId(clear, "action:roles.override.clear");
                clear.Click += async (_, _) => await CommitAsync(() => _api!.ClearModelRoleOverrideAsync(role, key, _token)).ConfigureAwait(true);
                row.OverridePanel.Children.Add(clear);
            }
            else
            {
                row.OverridePanel.Children.Add(new TextBlock { Text = Text("usage.set_by_the_project_config"), Classes = { "usage-caption" } });
            }
        }
    }

    /// <summary>Persists a role and rereads its effective assignment.</summary>
    /// <param name="id">Role identifier.</param>
    /// <param name="reference">New model reference.</param>
    /// <param name="effort">Optional reasoning effort.</param>
    /// <returns>The acknowledged operation.</returns>
    private Task ChangeRoleAsync(string id, string reference, string? effort) => CommitAsync(() => _api!.SetModelRoleAsync(id, reference, effort, _token));

    /// <summary>Prevents simultaneous edits and reloads authoritative state even on refused writes.</summary>
    /// <param name="write">Selected operation.</param>
    /// <returns>The write and reread.</returns>
    private async Task CommitAsync(Func<Task> write)
    {
        if (_busy || _token.IsCancellationRequested)
        {
            return;
        }

        _busy = true;
        UsageRows.IsEnabled = false;
        UsageFailure.IsVisible = false;
        try
        {
            await write().ConfigureAwait(true);
        }
        catch (Exception error)
        {
            Report(error);
        }

        try
        {
            await ReadAsync().ConfigureAwait(true);
        }
        catch (Exception error)
        {
            if (!UsageFailure.IsVisible)
            {
                Report(error);
            }
        }
        finally
        {
            _busy = false;
            if (!_token.IsCancellationRequested)
            {
                Draw();
                UsageRows.IsEnabled = true;
            }
        }
    }

    /// <summary>Reports why a setting was refused or why a saved choice is pending.</summary>
    /// <param name="error">Kernel failure.</param>
    private void Report(Exception error)
    {
        if (!_token.IsCancellationRequested)
        {
            var saved = error is KernelRefusal { Code: "runtime.saved_while_running" or "runtime.rebuild_failed" };
            UsageFailure.Text = (saved ? Text("common.saved_not_applied_yet") + " · " : string.Empty) + _describe!(error);
            UsageFailure.IsVisible = true;
        }
    }

    /// <summary>Builds the account and credential groupings shared with the model service rail.</summary>
    /// <returns>Groups in service order and with declared model routes.</returns>
    private UsageAccount[] Accounts()
    {
        var known = _services?.UsageAccounts.ToArray() ?? [];
        var order = _services?.UsageOrder.ToArray() ?? [];
        var routes = _models.GroupBy(model => (model.Vendor ?? model.Provider) + "\0" + (model.KeyEnv ?? string.Empty).Trim());
        return routes.Select(group =>
        {
            var account = known.FirstOrDefault(item => item.Key == group.Key);
            var first = group.First();
            return new UsageAccount(group.Key, account?.Label ?? first.DisplayName ?? first.Provider, group.ToArray());
        }).OrderBy(group =>
        {
            var rank = Array.FindIndex(order, provider => group.Models.Any(model => model.Provider == provider));
            return rank < 0 ? int.MaxValue : rank;
        }).ToArray();
    }

    /// <summary>Offers the current protocol per source and retains the stored selection.</summary>
    /// <param name="groups">The service groups.</param>
    /// <param name="role">The row's model purpose.</param>
    /// <param name="main">Running model.</param>
    /// <param name="value">Selected model.</param>
    /// <param name="empty">Optional inherited or disabled entry.</param>
    /// <returns>Native choices with nonselectable group headings.</returns>
    private ComboBoxItem[] Choices(UsageAccount[] groups, string role, string main, string value, string? empty)
    {
        var items = new List<ComboBoxItem>();
        if (empty is not null)
        {
            items.Add(new ComboBoxItem { Tag = string.Empty, Content = empty });
        }

        foreach (var group in groups)
        {
            var kind = _services?.PreferredProtocol(group.Key) ?? group.Models.FirstOrDefault(model => model.Ref == main)?.Kind
                ?? group.Models.FirstOrDefault(model => model.Default)?.Kind ?? group.Models[0].Kind ?? "openai";
            var offered = group.Models.Where(model => Matches(model, role)
                && (role == "web_search" || model.Ref == value || (model.Kind ?? "openai") == kind)).ToArray();
            if (offered.Length == 0)
            {
                continue;
            }

            items.Add(new ComboBoxItem { Content = group.Label, IsEnabled = false, FontWeight = Avalonia.Media.FontWeight.SemiBold });
            foreach (var model in offered)
            {
                items.Add(new ComboBoxItem { Tag = model.Ref, Content = model.Vision ? Shown(model) + " · " + Text("common.reads_images") : Shown(model) });
            }
        }

        if (value.Length > 0 && !items.Any(item => Equals(item.Tag, value)))
        {
            var title = MessageCatalog.Get(role == "vision" ? "usage.model_not_declared_to_read" : "usage.model_unavailable", _language, ("model", value));
            items.Insert(empty is null ? 0 : 1, new ComboBoxItem { Tag = value, Content = title });
        }

        return items.ToArray();
    }

    /// <summary>Looks up an existing model without manufacturing capabilities.</summary>
    /// <param name="reference">Model reference.</param>
    /// <returns>The catalog route.</returns>
    private ModelChoice? ByRef(string? reference) => _models.FirstOrDefault(model => model.Ref == reference);

    /// <summary>Names the group that owns a route.</summary>
    /// <param name="groups">Available service groups.</param>
    /// <param name="reference">Model reference.</param>
    /// <returns>Service label.</returns>
    private string Service(UsageAccount[] groups, string reference) =>
        groups.FirstOrDefault(group => group.Models.Any(model => model.Ref == reference))?.Label ?? reference;

    /// <summary>Formats the catalog-declared features of a route.</summary>
    /// <param name="model">Optional model.</param>
    /// <returns>Display summary.</returns>
    private string Traits(ModelChoice? model)
    {
        if (model is null)
        {
            return string.Empty;
        }

        var values = new List<string>();
        if (model.Vision)
        {
            values.Add(Text("common.reads_images"));
        }

        if (model.Efforts is { Length: > 1 })
        {
            values.Add(Text("usage.adjustable_reasoning"));
        }

        if (model.ContextWindow > 0)
        {
            var size = model.ContextWindow >= 1000000
                ? (model.ContextWindow / 1000000d).ToString("0.#", CultureInfo.InvariantCulture) + "M"
                : Math.Round(model.ContextWindow / 1024d, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) + "K";
            values.Add(MessageCatalog.Get("usage.size_context", _language, ("size", size)));
        }

        return string.Join(" · ", values);
    }

    /// <summary>Reads a settings caption.</summary>
    /// <param name="key">Message key.</param>
    /// <returns>The localized caption.</returns>
    private string Text(string key) => MessageCatalog.Get(key, _language);

    /// <summary>A credential-separated group of model choices.</summary>
    /// <param name="Key">Stable account identity.</param>
    /// <param name="Label">Reference group label.</param>
    /// <param name="Models">Models advertised by the account.</param>
    private sealed record UsageAccount(string Key, string Label, ModelChoice[] Models);
}
