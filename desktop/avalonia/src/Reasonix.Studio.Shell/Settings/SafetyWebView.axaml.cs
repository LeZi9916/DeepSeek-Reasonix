using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Edits the ordered web backends and the built-in browser preference.</summary>
public sealed partial class SafetyWebView : SettingsFormView
{
    /// <summary>Initializes a new instance of the <see cref="SafetyWebView"/> class.</summary>
    public SafetyWebView() => InitializeComponent();

    /// <summary>Reads the backend and browser settings independently.</summary>
    /// <param name="api">The active kernel endpoints.</param>
    /// <param name="language">The interface language.</param>
    /// <param name="describe">Localizes the kernel's refusal.</param>
    /// <param name="token">The window lifetime.</param>
    /// <returns>The two initial reads.</returns>
    internal async Task OpenAsync(StudioPageClient api, InterfaceLanguage language, Func<Exception, string> describe, CancellationToken token)
    {
        Translate(language);
        var web = new JsonObject();
        var browser = new JsonObject();
        var busy = false;
        var probing = false;
        var pendingWrite = Task.CompletedTask;
        var browserBusy = false;
        var rendering = false;
        var renderingBrowser = false;
        var locked = false;
        Url.TextChanged += (_, _) => UpdateProbe();
        Url.LostFocus += async (_, _) =>
        {
            var value = Url.Text?.Trim() ?? string.Empty;
            if (!rendering && value != (string?)web["searxng_url"])
            {
                await ApplyAsync("searxng_url", JsonValue.Create(value)).ConfigureAwait(true);
            }
        };
        Probe.Click += async (_, _) =>
        {
            if (probing || locked)
            {
                return;
            }

            var value = Url.Text?.Trim() ?? string.Empty;
            probing = true;
            Probe.Content = Text("models.testing");
            ProbeStatus.IsVisible = false;
            UpdateProbe();
            try
            {
                // The test click may also move focus out of the address field.
                await pendingWrite.ConfigureAwait(true);
                await RunWebAsync(async () =>
                {
                    if (value != (string?)web["searxng_url"])
                    {
                        web = await api.SaveWebAccessAsync(new JsonObject { ["searxng_url"] = value }, token).ConfigureAwait(true);
                    }

                    var probe = await api.ProbeSearxngAsync(token).ConfigureAwait(true);
                    ShowProbe(probe);
                }).ConfigureAwait(true);
            }
            finally
            {
                probing = false;
                Probe.Content = Text("common.test_it");
                UpdateProbe();
            }
        };
        Video.IsCheckedChanged += async (_, _) =>
        {
            if (!rendering)
            {
                await ApplyAsync("video_url_passthrough", JsonValue.Create(Video.IsChecked == true)).ConfigureAwait(true);
            }
        };
        Browser.IsCheckedChanged += async (_, _) =>
        {
            if (!renderingBrowser && !browserBusy)
            {
                browserBusy = true;
                Browser.IsEnabled = false;
                BrowserError.IsVisible = false;
                try
                {
                    browser = await api.SaveBrowserToolsAsync(Browser.IsChecked == true, token).ConfigureAwait(true);
                }
                catch (Exception error)
                {
                    if (!token.IsCancellationRequested)
                    {
                        BrowserError.Text = describe(error);
                        BrowserError.IsVisible = true;
                    }
                }
                finally
                {
                    RenderBrowser();
                    Browser.IsEnabled = true;
                    browserBusy = false;
                }
            }
        };
        await Task.WhenAll(ReadWebAsync(), ReadBrowserAsync()).ConfigureAwait(true);

        async Task ReadWebAsync()
        {
            try
            {
                web = await api.WebAccessAsync(token).ConfigureAwait(true);
                WebSection.IsVisible = true;
                RenderWeb();
            }
            catch (Exception)
            {
                if (!token.IsCancellationRequested)
                {
                    WebSection.IsVisible = false;
                }
            }
        }

        async Task ReadBrowserAsync()
        {
            try
            {
                browser = await api.BrowserToolsAsync(token).ConfigureAwait(true);
                RenderBrowser();
            }
            catch (Exception)
            {
                Browser.IsEnabled = false;
                BrowserError.Text = Text("workbench.could_not_read_the_built");
                BrowserError.IsVisible = !token.IsCancellationRequested;
            }
        }

        Task ApplyAsync(string key, JsonNode? value) => RunWebAsync(async () =>
        {
            web = await api.SaveWebAccessAsync(new JsonObject { [key] = value }, token).ConfigureAwait(true);
        });

        async Task RunWebAsync(Func<Task> edit)
        {
            if (busy || locked)
            {
                return;
            }

            var completion = new TaskCompletionSource();
            pendingWrite = completion.Task;
            busy = true;
            SetWebEditorsEnabled(false);
            WebError.IsVisible = false;
            try
            {
                await edit().ConfigureAwait(true);
                RenderWeb();
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    locked = error is KernelRefusal { Code: "web_access.editing_disabled" };
                    WebError.Text = describe(error);
                    WebError.IsVisible = true;
                    RenderWeb();
                }
            }
            finally
            {
                busy = false;
                ReadOnly.IsVisible = locked;
                SetWebEditorsEnabled(!locked);
                UpdateProbe();
                completion.SetResult();
            }
        }

        void RenderBrowser()
        {
            renderingBrowser = true;
            var enabled = (bool?)browser["enabled"] == true;
            var effective = (bool?)browser["effective"] == true;
            Browser.IsChecked = enabled;
            BrowserStatus.IsVisible = browser["effective"] is not null && enabled != effective;
            BrowserStatus.Text = Text("common.actually_in_force") + " · " + Text(effective
                ? "workbench.this_projects_config_file_turns_2"
                : "workbench.this_projects_config_file_turns");
            renderingBrowser = false;
        }

        void RenderWeb()
        {
            rendering = true;
            var enabled = Strings(web["search_backends"]).ToArray();
            Url.Text = (string?)web["searxng_url"] ?? string.Empty;
            Searxng.IsVisible = enabled.Contains("searxng", StringComparer.Ordinal);
            Video.IsChecked = (bool?)web["video_url_passthrough"] == true;
            WebPath.Text = Text("models.a_saved_change_reaches_only") + (string?)web["path"];
            RenderBackends(SearchBackends, "search", "common.web_search");
            RenderBackends(FetchBackends, "fetch", "models.web_fetch");
            rendering = false;
        }

        void RenderBackends(StackPanel rows, string kind, string title)
        {
            var selection = kind + "_backends";
            var permissions = kind + "_paid_allowed";
            var known = Strings(web["known_" + selection]).ToArray();
            var enabled = Strings(web[selection]).ToArray();
            var allowed = Strings(web[permissions]).ToArray();
            var paidBackends = Strings(web["paid_backends"]).ToHashSet(StringComparer.Ordinal);
            rows.Children.Clear();
            rows.Children.Add(new TextBlock
            {
                Text = Text(title) + " " + Text("models.tried_in_order_the_free"),
                Classes = { "safety-hint" },
                Margin = new Thickness(12, 6),
            });
            if (enabled.Length == 0)
            {
                rows.Children.Add(new TextBlock
                {
                    Text = Text(kind == "search" ? "models.no_search_backend_is_on" : "models.no_fetch_backend_is_on"),
                    Classes = { "safety-hint" },
                    Margin = new Thickness(12, 6),
                });
            }

            foreach (var id in enabled.Where(known.Contains).Concat(known.Except(enabled)))
            {
                var index = Array.IndexOf(enabled, id);
                var active = index >= 0;
                var (name, note) = Backend(kind, id);
                var toggle = new ToggleSwitch { Tag = kind + ":" + id, IsChecked = active };
                AutomationProperties.SetName(toggle, Text("common.enable_name", ("name", name)));
                AutomationProperties.SetAutomationId(toggle, "action:web-access.backend");
                toggle.IsCheckedChanged += async (_, _) =>
                {
                    var updated = toggle.IsChecked == true ? enabled.Append(id) : enabled.Where(value => value != id);
                    await ApplyAsync(selection, ArrayOf(updated)).ConfigureAwait(true);
                };
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                if (active)
                {
                    actions.Children.Add(Move(-1, "up", "↑"));
                    actions.Children.Add(Move(1, "down", "↓"));
                }

                actions.Children.Add(toggle);
                var content = new StackPanel { Spacing = 6, Children = { Row(name, note, actions) } };
                var consented = allowed.Contains(id, StringComparer.Ordinal);
                if (paidBackends.Contains(id) && (active || consented))
                {
                    var consentLabel = Text(kind == "search" ? "models.agree_to_be_billed_per_2" : "models.agree_to_be_billed_per");
                    var ready = (bool?)web["deepseek_api_ready"] == true;
                    var consent = new ToggleSwitch { Tag = kind + ":" + id, IsEnabled = ready || consented, IsChecked = consented };
                    AutomationProperties.SetName(consent, consentLabel);
                    AutomationProperties.SetAutomationId(consent, "action:web-access.consent");
                    consent.IsCheckedChanged += async (_, _) =>
                    {
                        var updated = consent.IsChecked == true ? allowed.Append(id) : allowed.Where(value => value != id);
                        await ApplyAsync(permissions, ArrayOf(updated)).ConfigureAwait(true);
                    };
                    var consentRow = Row(consentLabel, Text(ready || consented ? "models.once_you_agree_it_is" : "models.this_needs_a_deepseek_provider"), consent);
                    consentRow.Margin = new Thickness(14, 0, 0, 0);
                    content.Children.Add(consentRow);
                }

                rows.Children.Add(new Border { Classes = { "safety-line" }, Padding = new Thickness(12, 8), Child = content });

                Button Move(int offset, string direction, string glyph)
                {
                    var other = index + offset;
                    var move = new Button
                    {
                        Tag = kind + ":" + id + ":" + direction,
                        Content = glyph,
                        IsEnabled = other >= 0 && other < enabled.Length,
                        VerticalAlignment = VerticalAlignment.Center,
                        Classes = { "preset-act", "safety-action" },
                        Padding = new Thickness(8, 0),
                    };
                    AutomationProperties.SetAutomationId(move, "action:web-access.move");
                    AutomationProperties.SetName(move, Text("common.move_name_" + direction, ("name", name)));
                    move.Click += async (_, _) =>
                    {
                        var order = enabled.ToArray();
                        (order[index], order[other]) = (order[other], order[index]);
                        await ApplyAsync(selection, ArrayOf(order)).ConfigureAwait(true);
                    };
                    return move;
                }
            }
        }

        void SetWebEditorsEnabled(bool enabled) =>
            SearchBackends.IsEnabled = FetchBackends.IsEnabled = Video.IsEnabled = Url.IsEnabled = enabled;

        void UpdateProbe() => Probe.IsEnabled = !probing && !locked && !string.IsNullOrWhiteSpace(Url.Text);

        void ShowProbe(JsonObject result)
        {
            var code = (string?)result["code"];
            var key = ((bool?)result["ok"] == true, code) switch
            {
                (true, _) => "models.connected_this_searxng_returns_json",
                (_, "search.searxng_json_disabled") => "models.this_searxng_does_not_allow",
                (_, "search.searxng_unreachable") => "models.cannot_reach_this_address_check",
                (_, "search.searxng_not_searxng") => "models.this_address_answers_but_not",
                (_, "search.searxng_redirected") => "models.this_address_redirects_elsewhere_enter",
                (_, "search.searxng_address_refused") => "models.the_test_will_not_connect",
                _ => "models.this_searxng_refused_the_test",
            };
            ProbeStatus.Text = Text(key, ("status", result["status"]?.ToString() ?? string.Empty));
            ProbeStatus.IsVisible = true;
        }
    }

    /// <summary>Reads backend names from the response.</summary>
    /// <param name="node">The order or permission list.</param>
    /// <returns>The unchanged IDs.</returns>
    private static IEnumerable<string> Strings(JsonNode? node) => (node as JsonArray)?.Select(item => (string?)item).OfType<string>() ?? [];

    /// <summary>Encodes a backend list for a settings write.</summary>
    /// <param name="values">The ordered IDs.</param>
    /// <returns>The kernel payload value.</returns>
    private static JsonArray ArrayOf(IEnumerable<string> values) => new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());

    /// <summary>Lays out an enabled backend or its separate billing control.</summary>
    /// <param name="name">The visible label.</param>
    /// <param name="note">The backend explanation.</param>
    /// <param name="controls">The right-side switches and ordering actions.</param>
    /// <returns>The row.</returns>
    private static Grid Row(string name, string note, Control controls)
    {
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = name, Classes = { "safety-title" }, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = note, Classes = { "safety-hint" }, IsVisible = !string.IsNullOrEmpty(note) },
            },
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10, Children = { text, controls } };
        controls.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(controls, 1);
        return grid;
    }

    /// <summary>Names the backend using the same labels as the web settings in the reference.</summary>
    /// <param name="kind">Search or fetch.</param>
    /// <param name="id">The backend ID.</param>
    /// <returns>The label and explanatory text.</returns>
    private (string Name, string Note) Backend(string kind, string id) => (kind, id) switch
    {
        ("search", "searxng") => ("SearXNG", Text("models.a_search_service_you_run")),
        ("search", "deepseek_api") => (Text("models.deepseek_search"), Text("models.deepseeks_own_search_api_billed")),
        ("fetch", "local") => (Text("models.local_fetch"), Text("models.this_machine_downloads_the_page")),
        ("fetch", "deepseek_api") => (Text("models.deepseek_fetch"), Text("models.deepseeks_own_page_fetch_api")),
        _ => (id, string.Empty),
    };
}
