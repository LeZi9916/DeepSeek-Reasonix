using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
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

/// <summary>The Automation hook recipes and scope-specific editor.</summary>
public sealed partial class HookSettingsView : SettingsFormView
{
    /// <summary>Rule recipes defined by the reference settings UI.</summary>
    private static readonly Recipe[] s_recipes =
    [
        new("format", "ext.format_files_after_they_are", "ext.runs_a_formatter_after_every", "PostToolUse", "edit_file|write_file|multi_edit", "gofmt -w . 2>/dev/null || true"),
        new("guard-secrets", "ext.ask_me_before_writing_a", "ext.stops_on_paths_like_env", "PreToolUse", "edit_file|write_file", """grep -q '"\.env' <<< "$REASONIX_HOOK_PAYLOAD" && exit 2 || exit 0"""),
        new("test-before-stop", "ext.run_the_tests_before_wrapping", "ext.runs_the_tests_at_the", "Stop", null, "go test ./... 2>&1 | tail -5"),
    ];

    /// <summary>Initializes a new instance of the <see cref="HookSettingsView"/> class.</summary>
    public HookSettingsView() => InitializeComponent();

    /// <summary>Loads the scope and rules from the active session.</summary>
    /// <param name="api">The kernel hook endpoints.</param>
    /// <param name="language">The display language.</param>
    /// <param name="describe">Localizes a failure.</param>
    /// <param name="token">The host lifetime.</param>
    /// <returns>The initial rule read.</returns>
    internal async Task OpenAsync(StudioPageClient api, InterfaceLanguage language, Func<Exception, string> describe, CancellationToken token)
    {
        Translate(language);
        HookHint.Text = Text("settings.hooks_hint");
        UserScope.Tag = "user";
        ProjectScope.Tag = "project";
        AutomationProperties.SetName(UserScope, Text("ext.mine"));
        AutomationProperties.SetName(ProjectScope, Text("ext.this_project"));
        var catalog = new JsonObject();
        var draft = new List<JsonObject>();
        var busy = false;
        var scope = "user";
        UserScope.IsCheckedChanged += (_, _) => SelectScope(UserScope);
        ProjectScope.IsCheckedChanged += (_, _) => SelectScope(ProjectScope);
        More.Click += (_, _) => SetExpert(!Expert.IsVisible);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && Expert.IsVisible && !busy)
            {
                e.Handled = true;
                SetExpert(false);
            }
        };
        AddRule.Click += (_, _) =>
        {
            draft.Add(new JsonObject { ["event"] = "PostToolUse", ["command"] = string.Empty, ["scope"] = scope });
            RenderDraft();
        };
        SaveRules.Click += async (_, _) => await SaveAsync(draft).ConfigureAwait(true);
        try
        {
            if (await api.HooksAsync(token).ConfigureAwait(true) is { } loaded)
            {
                catalog = loaded;
                Controls.IsVisible = true;
                RenderScope();
            }
            else
            {
                ShowError(Text("ext.could_not_read_the_hooks"));
            }
        }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested)
            {
                ShowError(describe(error));
            }
        }

        void SelectScope(RadioButton choice)
        {
            if (!busy && choice.IsChecked == true && choice.Tag is string selected && selected != scope)
            {
                scope = selected;
                RenderScope();
            }
        }

        void SetExpert(bool open)
        {
            Expert.IsVisible = open;
            MoreLabel.Text = Text(open ? "common.collapse" : "ext.write_one_manually");
            if (open)
            {
                RenderDraft();
            }
            else
            {
                draft = Mine();
                More.Focus();
            }
        }

        List<JsonObject> Mine() => Items(catalog["hooks"])
            .Where(rule => (string?)rule["scope"] == (scope == "user" ? "global" : "project")).Select(rule => rule.DeepClone().AsObject()).ToList();

        void RenderScope()
        {
            var path = (string?)catalog[scope == "user" ? "globalPath" : "projectPath"];
            var all = Items(catalog["hooks"]).ToArray();
            var hint = Text("settings.hooks_hint");
            HookHint.Text = all.Length == 0 ? hint : hint + " · " + Text("settings.n_hooks", ("n", Count(all.Length)));
            ProjectScope.IsEnabled = !string.IsNullOrEmpty((string?)catalog["projectPath"]);
            PathLabel.Text = path ?? string.Empty;
            Sources.Children.Clear();
            foreach (var item in Items(catalog["sources"]).Where(item => (string?)item["status"] is "malformed" or "unreadable"))
            {
                Sources.Children.Add(Caption($"{item["path"]} {Text("ext.could_not_be_read")}{item["parseError"] ?? item["status"]}"));
            }

            draft = Mine();
            MoreLabel.Text = Text(Expert.IsVisible ? "common.collapse" : "ext.write_one_manually");
            RuleCount.Text = Text("common.n_rules", ("n", Count(draft.Count)));
            Recipes.Children.Clear();
            foreach (var recipe in s_recipes)
            {
                var toggle = new ToggleSwitch { IsChecked = draft.Exists(recipe.Matches), Tag = recipe.Id };
                AutomationProperties.SetAutomationId(toggle, "action:hooks.recipe");
                AutomationProperties.SetName(toggle, Text(recipe.Title));
                toggle.IsCheckedChanged += async (_, _) =>
                {
                    if (busy)
                    {
                        return;
                    }

                    var stored = Mine();
                    if (toggle.IsChecked == true)
                    {
                        stored.Add(recipe.NewRule(scope));
                    }
                    else
                    {
                        stored.RemoveAll(rule => recipe.Matches(rule));
                    }

                    await SaveAsync(stored).ConfigureAwait(true);
                };
                var text = new StackPanel
                {
                    Spacing = 2,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = Text(recipe.Title), LineHeight = 19, TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Text = Text(recipe.Description), Classes = { "hook-hint" } },
                    },
                };
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    ColumnSpacing = 12,
                    Children = { text, toggle },
                };
                Grid.SetColumn(toggle, 1);
                Recipes.Children.Add(new Border
                {
                    Child = row,
                    Padding = new Thickness(12, 10),
                    BorderThickness = new Thickness(0, 1, 0, recipe == s_recipes[^1] ? 1 : 0),
                    [!Border.BorderBrushProperty] = StudioTypography.Resource("HairBrush"),
                });
            }

            if (Expert.IsVisible)
            {
                RenderDraft();
            }
        }

        void RenderDraft()
        {
            RuleList.Children.Clear();
            var events = Items(catalog["events"]).ToArray();
            foreach (var rule in draft.ToArray())
            {
                var editor = new HookRuleEditor();
                editor.Open(rule, events, Language, EventLabel, TryAsync, () => SaveAsync(draft.Where(other => !ReferenceEquals(rule, other))));
                RuleList.Children.Add(editor);
            }

            PluginRules.Children.Clear();
            var plugins = Items(catalog["hooks"]).Where(rule => (string?)rule["scope"] == "plugin").ToArray();
            if (plugins.Length > 0)
            {
                PluginRules.Children.Add(Caption(Text("ext.n_from_plugins", ("n", Count(plugins.Length)))));
                foreach (var plugin in plugins)
                {
                    PluginRules.Children.Add(Caption(EventLabel((string?)plugin["event"] ?? string.Empty) + "  " + plugin["command"] + "  " + Text("common.read_only")));
                }
            }
        }

        async Task SaveAsync(IEnumerable<JsonObject> rules)
        {
            var saved = await RunAsync(async () =>
            {
                await api.SetHooksAsync(scope, new JsonArray(rules.Select(rule => rule.DeepClone()).ToArray()), token).ConfigureAwait(true);
                if (await api.HooksAsync(token).ConfigureAwait(true) is { } loaded)
                {
                    catalog = loaded;
                    RenderScope();
                }

                return true;
            }).ConfigureAwait(true);
            if (!saved)
            {
                var current = Mine();
                foreach (var toggle in Recipes.GetVisualDescendants().OfType<ToggleSwitch>())
                {
                    var recipe = s_recipes.FirstOrDefault(recipe => Equals(toggle.Tag, recipe.Id));
                    if (recipe is not null)
                    {
                        busy = true;
                        toggle.IsChecked = current.Exists(recipe.Matches);
                        busy = false;
                    }
                }
            }
        }

        Task<JsonObject?> TryAsync(JsonObject rule) => RunAsync<JsonObject?>(async () => await api.TryHookAsync(rule, token).ConfigureAwait(true));

        async Task<T> RunAsync<T>(Func<Task<T>> operation)
        {
            if (busy)
            {
                return default!;
            }

            busy = true;
            Controls.IsEnabled = false;
            ShowError(string.Empty);
            try
            {
                return await operation().ConfigureAwait(true);
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    ShowError(describe(error));
                }

                return default!;
            }
            finally
            {
                Controls.IsEnabled = true;
                busy = false;
            }
        }

        void ShowError(string error)
        {
            Error.Text = error;
            Error.IsVisible = error.Length > 0;
        }
    }

    /// <summary>Returns objects from a hook catalog list.</summary>
    /// <param name="node">The list on the wire.</param>
    /// <returns>The non-null entries.</returns>
    private static IEnumerable<JsonObject> Items(JsonNode? node) => (node as JsonArray)?.OfType<JsonObject>() ?? [];

    /// <summary>Formats a localized count argument.</summary>
    /// <param name="count">The number of rules.</param>
    /// <returns>The count.</returns>
    private static string Count(int count) => count.ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>Explains a hook event while preserving its canonical name.</summary>
    /// <param name="value">The event ID.</param>
    /// <returns>The readable label.</returns>
    private string EventLabel(string value)
    {
        var key = value switch
        {
            "PreToolUse" => "ext.before_a_tool_runs",
            "PostToolUse" => "ext.after_a_tool_runs",
            "PostToolUseFailure" => "ext.after_a_tool_fails",
            "PermissionRequest" => "ext.when_permission_is_requested",
            "UserPromptSubmit" => "ext.when_a_message_is_submitted",
            "Stop" => "ext.when_the_turn_ends",
            "StopFailure" => "ext.when_the_turn_fails",
            "PostLLMCall" => "ext.after_a_model_response_completes",
            "SessionStart" => "ext.when_a_session_starts",
            "SessionEnd" => "ext.when_a_session_ends",
            "SubagentStart" => "ext.when_a_subagent_starts",
            "SubagentStop" => "ext.when_a_subagent_ends",
            "Notification" => "ext.when_your_attention_is_needed",
            "PreCompact" => "ext.before_context_is_compacted",
            _ => null,
        };
        return key is null ? value : Text(key) + " (" + value + ")";
    }

    /// <summary>A selectable recipe from the reference hook editor.</summary>
    /// <param name="Id">The automation target ID.</param>
    /// <param name="Title">The displayed label.</param>
    /// <param name="Description">The displayed explanation.</param>
    /// <param name="Event">The event to attach.</param>
    /// <param name="Match">The optional matcher text.</param>
    /// <param name="Command">The rule's command text.</param>
    private sealed record Recipe(string Id, string Title, string Description, string Event, string? Match, string Command)
    {
        /// <summary>Checks whether a scope already contains the recipe.</summary>
        /// <param name="rule">A saved rule.</param>
        /// <returns>Whether it matches this recipe.</returns>
        public bool Matches(JsonObject rule) => (string?)rule["event"] == Event && (string?)rule["command"] == Command;

        /// <summary>Builds a rule in the chosen scope.</summary>
        /// <param name="scope">User or project.</param>
        /// <returns>The new rule.</returns>
        public JsonObject NewRule(string scope)
        {
            var rule = new JsonObject { ["event"] = Event, ["command"] = Command, ["description"] = Title, ["scope"] = scope };
            if (Match is not null)
            {
                rule["match"] = Match;
            }

            return rule;
        }
    }
}
