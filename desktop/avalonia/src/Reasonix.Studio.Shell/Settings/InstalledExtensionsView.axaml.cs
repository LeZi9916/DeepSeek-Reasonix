using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
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

/// <summary>The installed plugin, standalone service, and skill inventories for settings.</summary>
internal sealed partial class InstalledExtensionsView : UserControl
{
    /// <summary>Rows currently visible in the plugin card.</summary>
    private readonly Dictionary<JsonObject, ExtensionInventoryRow> _pluginRows = [];

    /// <summary>Source entry lives above the inventory search in the plugin group.</summary>
    private readonly ContentControl _addHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, IsVisible = false };

    /// <summary>Holds the add-service form while the existing rows refresh.</summary>
    private readonly ContentControl _serviceAddHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, IsVisible = false };

    /// <summary>Shows scoped inventory owned by the current or selected workspace.</summary>
    private readonly StackPanel _serviceRows = new();

    /// <summary>Shows unbundled skills by origin.</summary>
    private readonly StackPanel _skillRows = new();

    /// <summary>Holds package rows and preserves their disclosure state while filtering.</summary>
    private readonly StackPanel _packageRows = new();

    /// <summary>Searches the plugin names and contributions.</summary>
    private readonly TextBox _search = new() { Classes = { "settings-input" }, Height = 30 };

    /// <summary>Clears a plugin search and restores focus.</summary>
    private readonly Button _clear = new() { Classes = { "extension-action" }, IsVisible = false };

    /// <summary>Reports results relative to the total inventory.</summary>
    private readonly TextBlock _count = new() { Classes = { "extension-caption" }, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Explains an empty filtered list.</summary>
    private readonly TextBlock _empty = new() { Classes = { "extension-caption" }, IsVisible = false, Margin = new Thickness(0, 6) };

    /// <summary>Known workspace choices.</summary>
    private JsonObject[] _scopes = [];

    /// <summary>The resolved workspace shown in the toolbar.</summary>
    private JsonObject? _scope;

    /// <summary>Explicit root, or null when following the runtime.</summary>
    private string? _root;

    /// <summary>Current host client.</summary>
    private StudioPageClient? _api;

    /// <summary>Notifies the toolbar about the currently mounted workspace.</summary>
    private Action<JsonObject?, JsonObject[]>? _scopeChanged;

    /// <summary>Localizes failures.</summary>
    private Func<Exception, string>? _describe;

    /// <summary>Settings language.</summary>
    private InterfaceLanguage _language;

    /// <summary>Window lifetime.</summary>
    private CancellationToken _token;

    /// <summary>The latest scoped-inventory read.</summary>
    private int _read;

    /// <summary>Initializes a new instance of the <see cref="InstalledExtensionsView"/> class.</summary>
    public InstalledExtensionsView()
    {
        InitializeComponent();
        SizeChanged += (_, e) =>
        {
            var compact = e.NewSize.Width <= 500;
            Packages.SetCompact(compact);
            Services.SetCompact(compact);
            Skills.SetCompact(compact);
        };
    }

    /// <summary>Reads installed packages, external services and skills from the active host.</summary>
    /// <param name="api">The configured runtime.</param>
    /// <param name="language">Settings language.</param>
    /// <param name="describe">A failure formatter.</param>
    /// <param name="scopeChanged">Updates the workspace selection pill.</param>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The initial inventory.</returns>
    internal async Task OpenAsync(
        StudioPageClient api,
        InterfaceLanguage language,
        Func<Exception, string> describe,
        Action<JsonObject?, JsonObject[]> scopeChanged,
        CancellationToken token)
    {
        _api = api;
        _language = language;
        _token = token;
        _describe = describe;
        _scopeChanged = scopeChanged;
        _search.PlaceholderText = Text("ext.search_package_names_skills_commands");
        AutomationProperties.SetName(_search, Text("ext.search_installed_packages"));
        AutomationProperties.SetAutomationId(_search, "action:extensions.search");
        AutomationProperties.SetAutomationId(_clear, "action:extensions.clear-search");
        _clear.Content = Text("common.clear_search");
        _clear.Click += (_, _) =>
        {
            _search.Text = string.Empty;
            _search.Focus();
        };
        _search.TextChanged += (_, _) => FilterPackages();
        var search = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 10, Children = { _search, _clear, _count } };
        Grid.SetColumn(_clear, 1);
        Grid.SetColumn(_count, 2);
        var packageList = new StackPanel { Children = { _addHost, search, _empty, new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = _packageRows } } };
        ((Border)packageList.Children[^1]).Bind(Border.BorderBrushProperty, this.GetResourceObservable("HairBrush"));
        _packageRows.Margin = new Thickness(0, 12, 0, 0);
        Packages.Open(Text("settings.plugin_packages"), Text("settings.one_package_can_bring_skills"), null, packageList);
        Services.Open(
            Text("ext.external_services"),
            Text("settings.mcp_services_you_connected_yourself"),
            Text("settings.you_choose_where"),
            new StackPanel { Children = { _serviceAddHost, _serviceRows } });
        Skills.Open(Text("settings.skills"), Text("settings.skills_from_the_working_directory"), null, _skillRows);
        Footnote.Text = Text("settings.changes_apply_from_the_next_turn");
        BackToScope.Content = Text("ext.back_to_the_current_project");
        BackToScope.Click += async (_, _) => await ChangeScopeAsync(null).ConfigureAwait(true);
        Reload.Content = Text("common.retry");
        Reload.Click += async (_, _) =>
        {
            if (await MutateAsync(() => api.ReloadExtensionsAsync(token)).ConfigureAwait(true))
            {
                Reload.IsVisible = false;
            }
        };

        await Task.WhenAll(LoadPackagesAsync(), ReadScopeChoicesAsync()).ConfigureAwait(true);
        await ChangeScopeAsync(null).ConfigureAwait(true);
    }

    /// <summary>Opens the old inline plugin source workflow.</summary>
    /// <param name="chooseFolder">Whether the folder selector opens first.</param>
    /// <returns>The optional first folder read.</returns>
    internal Task AddSourceAsync(bool chooseFolder) => OpenSourceAsync(null, chooseFolder);

    /// <summary>Opens the current workspace's external-service flow.</summary>
    /// <returns>The form is ready for a source.</returns>
    internal Task AddServiceAsync()
    {
        var form = new ExtensionServiceSource();
        _serviceAddHost.Content = form;
        _serviceAddHost.IsVisible = true;
        form.Open(
            _api!,
            _language,
            (string?)_scope?["root"] is { Length: > 0 },
            _describe!,
            () =>
            {
                _serviceAddHost.IsVisible = false;
                _serviceAddHost.Content = null;
            },
            () => ChangeScopeAsync(_root),
            _token);
        return Task.CompletedTask;
    }

    /// <summary>Finds the new capability in the refreshed installed inventory.</summary>
    /// <param name="kind">The installed action kind.</param>
    /// <param name="name">The installed capability name.</param>
    /// <returns>The corresponding row or its inventory group.</returns>
    internal Control InstalledTarget(string kind, string name)
    {
        var (rows, section) = kind switch
        {
            "skill" => (_skillRows, Skills),
            "mcp" => (_serviceRows, Services),
            _ => (_packageRows, Packages),
        };
        var entry = rows.Children.OfType<Control>().FirstOrDefault(row => Equals(row.Tag, name));
        if (entry is ExtensionInventoryRow capability)
        {
            capability.ShowDetails(true);
        }

        return entry ?? section;
    }

    /// <summary>Switches the read and writes of scoped inventories to a selected project.</summary>
    /// <param name="root">Explicit root, or null to follow the session.</param>
    /// <returns>The scoped inventories.</returns>
    internal async Task ChangeScopeAsync(string? root)
    {
        if (_api is not { } api)
        {
            return;
        }

        var read = ++_read;
        var services = api.ExtensionServicesAsync(root, _token);
        var skills = api.ExtensionSkillsAsync(root, _token);
        try
        {
            var serviceCatalog = await services.ConfigureAwait(true);
            var skillCatalog = await skills.ConfigureAwait(true);
            if (read != _read || _token.IsCancellationRequested)
            {
                return;
            }

            _root = root;
            var scope = serviceCatalog?["scope"] as JsonObject
                ?? skillCatalog?["scope"] as JsonObject
                ?? _scopes.FirstOrDefault(item => root is null ? (bool?)item["current"] == true : (string?)item["root"] == root);
            _scope = scope;
            _scopeChanged?.Invoke(scope, _scopes);
            Away.IsVisible = root is not null;
            AwayInfo.Text = Text("ext.managing_other_project", ("name", (string?)scope?["label"] ?? (string?)scope?["name"] ?? root ?? string.Empty));
            DrawServices(serviceCatalog);
            DrawSkills(skillCatalog);
        }
        catch (Exception error)
        {
            if (read == _read)
            {
                Report(error);
            }
        }
    }

    /// <summary>Provides the button style shared by inline package actions.</summary>
    /// <param name="label">Localized action caption.</param>
    /// <param name="id">Automation id if this action has a wire command.</param>
    /// <returns>The secondary button.</returns>
    private static Button ActionButton(string label, string? id = null)
    {
        var action = new Button { Content = label, Classes = { "extension-action" } };
        if (id is not null)
        {
            AutomationProperties.SetAutomationId(action, id);
        }

        return action;
    }

    /// <summary>Matches contribution names and descriptions, not just package headings.</summary>
    /// <param name="package">The advertised package.</param>
    /// <param name="query">Non-empty search query.</param>
    /// <returns>Whether a searchable field contains the query.</returns>
    private static bool Matches(JsonObject package, string query)
    {
        if (query.Length == 0)
        {
            return true;
        }

        var values = new List<string?> { (string?)package["name"] };
        foreach (var field in new[] { "skills", "commands", "agents", "prompts", "themes", "mcpServers", "hooks" })
        {
            foreach (var item in package[field]?.AsArray().OfType<JsonObject>() ?? [])
            {
                foreach (var key in new[] { "name", "displayName", "description", "invocation", "command", "url", "transport", "event", "contextFile" })
                {
                    values.Add((string?)item[key]);
                }
            }
        }

        if (package["runtime"] is JsonObject runtime)
        {
            values.Add((string?)runtime["command"]);
            values.AddRange(runtime["args"]?.AsArray().Select(item => (string?)item) ?? []);
            values.AddRange(runtime["tools"]?.AsArray().Select(item => (string?)item) ?? []);
        }

        return values.Any(value => value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>Reads available scopes without replacing a mounted inventory on an optional response.</summary>
    /// <returns>The picker sources.</returns>
    private async Task ReadScopeChoicesAsync()
    {
        try
        {
            var result = await _api!.ExtensionScopesAsync(_token).ConfigureAwait(true);
            _scopes = result?["scopes"]?.AsArray().OfType<JsonObject>().ToArray() ?? [];
        }
        catch (Exception)
        {
            // The MCP and skills replies can still identify the current scope.
        }
    }

    /// <summary>Reads the package rows and preserves disclosure across a real refresh.</summary>
    /// <returns>The installed inventory.</returns>
    private async Task LoadPackagesAsync()
    {
        var inventory = await _api!.PluginsAsync(_token).ConfigureAwait(true);
        var expanded = _pluginRows.Where(pair => pair.Value.IsExpanded).Select(pair => (string?)pair.Key["name"]).ToHashSet();
        _pluginRows.Clear();
        _packageRows.Children.Clear();
        foreach (var item in inventory.OfType<JsonObject>())
        {
            var name = (string?)item["name"] ?? string.Empty;
            var row = CreatePackage(item, name);
            row.ShowDetails(expanded.Contains(name) || item["error"] is not null);
            _pluginRows[item] = row;
            _packageRows.Children.Add(row);
        }

        Packages.Count(_pluginRows.Count > 0 ? Text("common.n", ("n", Number(_pluginRows.Count))) : null);
        if (_search.Parent is Control search)
        {
            search.IsVisible = _pluginRows.Count > 0;
        }

        FilterPackages();
    }

    /// <summary>Filters without recreating open rows or changing enabled states.</summary>
    private void FilterPackages()
    {
        var query = _search.Text?.Trim() ?? string.Empty;
        foreach (var (package, row) in _pluginRows)
        {
            row.IsVisible = Matches(package, query);
        }

        var shown = _pluginRows.Values.Count(row => row.IsVisible);
        _count.Text = Text("ext.packages_shown_total", ("shown", Number(shown)), ("total", Number(_pluginRows.Count)));
        _clear.IsVisible = query.Length > 0;
        _empty.Text = Text(_pluginRows.Count == 0 ? "settings.no_plugin_packages_installed" : "ext.no_matching_installed_packages");
        _empty.IsVisible = shown == 0;
    }

    /// <summary>Builds the package summary, actions and contributions.</summary>
    /// <param name="package">The configured package.</param>
    /// <param name="name">Package name.</param>
    /// <returns>The disclosure row.</returns>
    private ExtensionInventoryRow CreatePackage(JsonObject package, string name)
    {
        var fields = new[]
        {
            ("skills", "ext.n_skills", "ext.skills"), ("commands", "ext.n_commands", "ext.commands"), ("agents", "subagent.group.count", "ext.subagents"),
            ("prompts", "ext.n_prompts", "ext.prompts"), ("themes", "ext.n_palettes", "ext.themes"),
            ("hooks", "ext.n_hooks", "ext.n_hooks"), ("mcpServers", "settings.n_services", "ext.external_services"),
        };
        var counts = fields.Where(item => package[item.Item1] is JsonArray { Count: > 0 })
            .Select(item => Text(item.Item2, ("n", Number(package[item.Item1]!.AsArray().Count)))).ToList();
        if (package["runtime"] is JsonObject)
        {
            counts.Add(Text("ext.long_running_process"));
        }

        var summary = Join(
            counts.Count > 0 ? Join(counts.ToArray()) : Text("ext.nothing_usable_inside"),
            (string?)package["version"],
            (string?)package["manifestKind"],
            (string?)package["source"]);
        var row = new ExtensionInventoryRow();
        row.Present(
            name,
            summary,
            "ready",
            (bool?)package["enabled"] == true,
            "action:extension.enabled",
            _language,
            enabled => SetPackageAsync(name, enabled));
        foreach (var note in new[] { (string?)package["error"], (string?)package["statusReason"] }
            .Concat(package["warnings"]?.AsArray().Select(item => (string?)item) ?? []).Where(note => !string.IsNullOrEmpty(note)))
        {
            row.DetailItems.Children.Add(Caption(note!, true));
        }

        var actions = new WrapPanel { ItemSpacing = 6, LineSpacing = 5 };
        if ((string?)package["source"] is { Length: > 0 })
        {
            var update = ActionButton(Text("ext.update"), "action:extensions.update");
            update.Click += async (_, _) => await OpenSourceAsync(package, false).ConfigureAwait(true);
            actions.Children.Add(update);
        }

        var export = ActionButton(Text("ext.export"), "action:extensions.export");
        export.Click += async (_, _) => await ExportAsync(name).ConfigureAwait(true);
        actions.Children.Add(export);
        var remove = ActionButton(Text("common.remove"));
        actions.Children.Add(remove);
        row.DetailItems.Children.Add(actions);
        var confirm = new WrapPanel { ItemSpacing = 7, LineSpacing = 5, IsVisible = false };
        confirm.Children.Add(Caption(Text("ext.delete_name_the_skills_commands", ("name", name))));
        var cancel = ActionButton(Text("common.cancel"));
        var delete = ActionButton(Text("common.delete"), "action:extensions.remove");
        delete.Classes.Add("danger");
        confirm.Children.Add(cancel);
        confirm.Children.Add(delete);
        remove.Click += (_, _) => confirm.IsVisible = true;
        cancel.Click += (_, _) => confirm.IsVisible = false;
        delete.Click += async (_, _) =>
        {
            delete.IsEnabled = false;
            try
            {
                var result = await _api!.RemovePluginAsync(name, _token).ConfigureAwait(true);
                ShowReloadFailure((string?)result["reloadError"]);
                if ((bool?)result["applied"] == true || (bool?)result["ok"] == true)
                {
                    await LoadPackagesAsync().ConfigureAwait(true);
                    await ChangeScopeAsync(_root).ConfigureAwait(true);
                }
                else
                {
                    Failure.Text = (string?)result["error"] ?? (string?)result["next"] ?? Text("ext.it_could_not_be_removed");
                    Failure.IsVisible = true;
                }
            }
            catch (Exception error)
            {
                Report(error);
            }
            finally
            {
                delete.IsEnabled = true;
            }
        };
        row.DetailItems.Children.Add(confirm);

        if (package["runtime"] is JsonObject runtime)
        {
            var command = string.Join(" ", new[] { (string?)runtime["command"] }.Concat(runtime["args"]?.AsArray().Select(item => (string?)item) ?? []));
            row.DetailItems.Children.Add(Fact(Text("ext.long_running_process"), command));
        }

        foreach (var (field, _, label) in fields.Where(field => field.Item1 is not ("hooks" or "mcpServers")))
        {
            if (package[field] is not JsonArray { Count: > 0 } contributions)
            {
                continue;
            }

            row.DetailItems.Children.Add(Heading(Text(label)));
            foreach (var item in contributions.OfType<JsonObject>())
            {
                row.DetailItems.Children.Add(Fact((string?)item["invocation"] ?? (string?)item["name"] ?? string.Empty, (string?)item["description"]));
            }
        }

        foreach (var hook in package["hooks"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            row.DetailItems.Children.Add(Fact((string?)hook["event"] ?? string.Empty, (string?)hook["description"] ?? (string?)hook["command"]));
        }

        foreach (var server in package["mcpServers"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            var nameOfServer = (string?)server["displayName"] ?? (string?)server["name"] ?? string.Empty;
            row.DetailItems.Children.Add(Fact(nameOfServer, (string?)server["description"] ?? (string?)server["command"] ?? (string?)server["url"]));
        }

        return row;
    }

    /// <summary>Writes the package's enabled state without losing the disclosure or query.</summary>
    /// <param name="name">Installed package identity.</param>
    /// <param name="enabled">Requested state.</param>
    /// <returns>Whether the persisted state changed.</returns>
    private async Task<bool> SetPackageAsync(string name, bool enabled)
    {
        try
        {
            var reply = await _api!.SetPluginEnabledAsync(name, enabled, _token).ConfigureAwait(true);
            ShowReloadFailure((string?)reply?["reloadError"]);
            return true;
        }
        catch (Exception error)
        {
            Report(error);
            return false;
        }
    }

    /// <summary>Shows tool inventories for independently configured services only.</summary>
    /// <param name="catalog">Authoritative MCP inventory.</param>
    private void DrawServices(JsonObject? catalog)
    {
        var owned = _pluginRows.Keys.SelectMany(package => package["mcpServers"]?.AsArray().OfType<JsonObject>() ?? [])
            .Select(server => (string?)server["name"]).ToHashSet();
        var loose = (catalog?["servers"]?.AsArray().OfType<JsonObject>() ?? [])
            .Where(server => !owned.Contains((string?)server["name"])).ToArray();
        _serviceRows.Children.Clear();
        Services.Count(loose.Length > 0 ? Text("settings.n_services", ("n", Number(loose.Length))) : null);
        foreach (var server in loose)
        {
            var name = (string?)server["name"] ?? string.Empty;
            var tools = (int?)server["tools"] ?? server["toolList"]?.AsArray().Count ?? 0;
            var state = (string?)server["state"] ?? string.Empty;
            var status = state switch
            {
                "ready" => Text("common.connected"),
                "standby" => Text("ext.standby_starts_on_first_call"),
                "failed" => Text("ext.unreachable"),
                "disabled" => Text("ext.state_disabled"),
                _ => state,
            };
            var summary = Join(
                tools > 0 ? Text("ext.n_tools", ("n", Number(tools))) : null,
                status,
                (string?)server["transport"],
                (string?)server["source"],
                (int?)server["httpStatus"] is > 0 and int code ? $"HTTP {code}" : null);
            var row = new ExtensionInventoryRow();
            row.Present(
                name,
                summary,
                state,
                (bool?)server["enabled"] == true,
                "action:mcp.enabled",
                _language,
                enabled => MutateAsync(() => _api!.SetExtensionServiceEnabledAsync(name, enabled, _root, _token)));
            if ((string?)server["description"] is { Length: > 0 } description)
            {
                row.DetailItems.Children.Add(Caption(description));
            }

            if ((string?)server["error"] is { Length: > 0 } error)
            {
                row.DetailItems.Children.Add(Caption(error, true));
                row.ShowDetails(true);
            }

            foreach (var tool in server["toolList"]?.AsArray().OfType<JsonObject>() ?? [])
            {
                row.DetailItems.Children.Add(Fact((string?)tool["name"] ?? string.Empty, (string?)tool["description"]));
            }

            _serviceRows.Children.Add(row);
        }

        if (loose.Length == 0)
        {
            _serviceRows.Children.Add(Caption(Text("settings.no_external_services_connected")));
        }
    }

    /// <summary>Shows separately installed skills with their original source grouping.</summary>
    /// <param name="catalog">Authoritative skill inventory.</param>
    private void DrawSkills(JsonObject? catalog)
    {
        var skills = (catalog?["skills"]?.AsArray().OfType<JsonObject>() ?? []).Where(skill => string.IsNullOrEmpty((string?)skill["plugin"])).ToArray();
        _skillRows.Children.Clear();
        var on = skills.Count(skill => (bool?)skill["enabled"] == true);
        Skills.Count(skills.Length > 0 ? Text("settings.on_all_on", ("on", Number(on)), ("all", Number(skills.Length))) : null);
        foreach (var group in skills.GroupBy(skill => (string?)skill["scope"] ?? string.Empty)
            .OrderBy(group => group.Key switch { "global" => 0, "project" => 1, "custom" => 2, "builtin" => 3, _ => 4 }))
        {
            var scope = Text(group.Key switch
            {
                "global" => "ext.mine",
                "project" => "common.project",
                "custom" => "common.custom",
                "builtin" => "common.built_in",
                _ => "ext.other",
            });
            _skillRows.Children.Add(Heading(scope));
            foreach (var skill in group)
            {
                var identity = (string?)skill["name"] ?? string.Empty;
                var path = (string?)skill["path"];
                var removable = _root is null && path is not null && (string?)skill["removes"] is { Length: > 0 };
                var row = new ExtensionSkillRow();
                row.Present(
                    skill,
                    scope,
                    _language,
                    async next =>
                    {
                        if (!await MutateAsync(() => _api!.SetExtensionSkillEnabledAsync(identity, next, _root, _token)).ConfigureAwait(true))
                        {
                            return false;
                        }

                        await ChangeScopeAsync(_root).ConfigureAwait(true);
                        return true;
                    },
                    removable ? RemoveAsync : null);
                _skillRows.Children.Add(row);

                async Task RemoveAsync()
                {
                    if (await MutateAsync(() => _api!.RemoveExtensionSkillAsync(identity, group.Key, path!, _token)).ConfigureAwait(true))
                    {
                        await ChangeScopeAsync(_root).ConfigureAwait(true);
                    }
                }
            }
        }

        if (skills.Length == 0)
        {
            _skillRows.Children.Add(Caption(Text("settings.no_skills_in_this_working")));
        }
    }

    /// <summary>Opens the package editor above the inventory.</summary>
    /// <param name="package">Package to update, or null for adding.</param>
    /// <param name="folder">Whether to open the folder chooser first.</param>
    /// <returns>The source form setup.</returns>
    private Task OpenSourceAsync(JsonObject? package, bool folder)
    {
        var form = new PluginSourceView();
        _addHost.Content = form;
        _addHost.IsVisible = true;
        return form.OpenAsync(
            _api!,
            _language,
            package,
            folder,
            _describe!,
            () =>
            {
                _addHost.IsVisible = false;
                _addHost.Content = null;
            },
            async () =>
            {
                await LoadPackagesAsync().ConfigureAwait(true);
                await ChangeScopeAsync(_root).ConfigureAwait(true);
            },
            _token);
    }

    /// <summary>Exports into an explicitly selected local file.</summary>
    /// <param name="name">Package chosen by the reader.</param>
    /// <returns>The optional export.</returns>
    private async Task ExportAsync(string name)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanSave: true } storage)
        {
            return;
        }

        try
        {
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Text("ext.export"),
                SuggestedFileName = name + ".zip",
            }).ConfigureAwait(true);
            if (file is not null)
            {
                var (archive, required) = await _api!.ExportPluginAsync(name, _token).ConfigureAwait(true);
                await using var stream = await file.OpenWriteAsync().ConfigureAwait(true);
                await stream.WriteAsync(archive, _token).ConfigureAwait(true);
                var feedback = required.Length > 0
                    ? Text("ext.the_key_values_were_stripped", ("names", required))
                    : Text("ext.this_package_asks_for_no");
                Failure.Text = Text("ext.exported") + " · " + feedback;
                Failure.Classes.Remove("failure");
                Failure.IsVisible = true;
            }
        }
        catch (Exception error)
        {
            Report(error);
        }
    }

    /// <summary>Applies a write and retains the old control value on refusal.</summary>
    /// <param name="action">Host command.</param>
    /// <returns>Whether the command was accepted.</returns>
    private async Task<bool> MutateAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(true);
            return true;
        }
        catch (Exception error)
        {
            Report(error);
            return false;
        }
    }

    /// <summary>Displays deferred runtime reload after a persisted package change.</summary>
    /// <param name="failure">Host reload error, if any.</param>
    private void ShowReloadFailure(string? failure)
    {
        Reload.IsVisible = !string.IsNullOrEmpty(failure);
        if (Reload.IsVisible)
        {
            Failure.Text = Text("common.saved_not_applied_yet") + " · " + failure;
            Failure.Classes.Add("failure");
            Failure.IsVisible = true;
        }
    }

    /// <summary>Reports a command that did not go through.</summary>
    /// <param name="error">The refusal.</param>
    private void Report(Exception error)
    {
        if (!_token.IsCancellationRequested)
        {
            Failure.Text = _describe!(error);
            Failure.Classes.Add("failure");
            Failure.IsVisible = true;
        }
    }

    /// <summary>Formats a row's metadata with the reference separator.</summary>
    /// <param name="values">Optional visible metadata.</param>
    /// <returns>The compact label.</returns>
    private string Join(params string?[] values) => string.Join(" · ", values.Where(value => !string.IsNullOrWhiteSpace(value)));

    /// <summary>Builds one contribution or tool row.</summary>
    /// <param name="name">Invocation or tool name.</param>
    /// <param name="description">Optional description.</param>
    /// <returns>The contribution row.</returns>
    private Control Fact(string name, string? description)
    {
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 9 };
        var label = new TextBlock { Text = name, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        label.Bind(TextBlock.FontFamilyProperty, label.GetResourceObservable("WorkbenchMonoFamily"));
        label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("MutedBrush"));
        line.Children.Add(label);
        var copy = Caption(description ?? string.Empty);
        Grid.SetColumn(copy, 1);
        line.Children.Add(copy);
        return line;
    }

    /// <summary>Builds a group label in the contribution list.</summary>
    /// <param name="title">Caption.</param>
    /// <returns>The styled label.</returns>
    private TextBlock Heading(string title) => new() { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 12, Classes = { "extension-caption" }, Margin = new Thickness(0, 5, 0, 1) };

    /// <summary>Builds wrapping secondary copy.</summary>
    /// <param name="text">Caption.</param>
    /// <param name="failure">Whether the text reports a failure.</param>
    /// <returns>The styled label.</returns>
    private TextBlock Caption(string text, bool failure = false)
    {
        var caption = new TextBlock { Text = text, Classes = { "extension-caption" } };
        caption.Classes.Set("failure", failure);
        return caption;
    }

    /// <summary>Formats a localized numeric count.</summary>
    /// <param name="count">Count.</param>
    /// <returns>Text to interpolate into the translation.</returns>
    private string Number(int count) => count.ToString(CultureInfo.CurrentCulture);

    /// <summary>Translates installed-inventory copy.</summary>
    /// <param name="key">Catalog key.</param>
    /// <param name="values">Substitution values.</param>
    /// <returns>The localized label.</returns>
    private string Text(string key, params (string Name, string Value)[] values) => MessageCatalog.Get(key, _language, values);
}
