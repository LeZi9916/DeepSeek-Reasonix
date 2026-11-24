using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Controls;
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

/// <summary>The rule editor and outside-text switch over the kernel's shared permission record.</summary>
public sealed partial class PermissionSettingsView : SettingsFormView
{
    /// <summary>Canonical permission verdicts in evaluation order.</summary>
    private static readonly (string Id, string Label, string Brush)[] s_levels =
    [
        ("deny", "common.refuse", "ErrBrush"), ("ask", "safety.ask", "WarnBrush"), ("allow", "common.allow", "OkBrush"),
    ];

    /// <summary>The fallback choices for unmatched writes.</summary>
    private static readonly (string Id, string Label, string Hint)[] s_modes =
    [
        ("ask", "safety.ask_for_confirmation", "safety.a_write_no_rule_matched"),
        ("allow", "common.allow", "safety.a_write_matched_by_no"),
        ("deny", "common.refuse", "safety.a_write_no_list_matched"),
    ];

    /// <summary>Tool names offered by the rule editor.</summary>
    private static readonly (string Id, string Label)[] s_tools =
    [
        ("bash", "chat.commands"), ("file_mutation", "safety.writing_files_every_write_tool"),
        ("read_file", "safety.reading_files"), ("write_file", "safety.whole_file_writes"), ("edit_file", "safety.file_edits"),
        ("web_fetch", "safety.fetching_web_pages"), ("grep", "safety.full_text_search"), ("glob", "safety.finding_files"),
    ];

    /// <summary>Presets from the reference rule editor.</summary>
    private static readonly Recipe[] s_recipes =
    [
        new("secrets", "safety.keep_env_off_limits", "safety.file_tools_are_refused_outright", "deny", ["file_mutation(*.env*)", "read_file(*.env*)"]),
        new("push", "safety.never_push", "safety.change_anything_locally_pushing_stays", "deny", ["bash(git push:*)"]),
        new("history", "safety.never_rewrite_git_history", "safety.rebase_and_reset_can_eat", "deny", ["bash(git rebase:*)", "bash(git reset:*)"]),
        new("tests", "safety.tests_without_asking", "safety.test_commands_go_straight_through", "allow", ["bash(go test:*)", "bash(npm test:*)", "bash(pytest:*)"]),
    ];

    /// <summary>Initializes a new instance of the <see cref="PermissionSettingsView"/> class.</summary>
    public PermissionSettingsView() => InitializeComponent();

    /// <summary>Loads and connects the approval settings for this kernel.</summary>
    /// <param name="api">The active kernel client.</param>
    /// <param name="language">The interface language.</param>
    /// <param name="describe">The error translator.</param>
    /// <param name="token">The window lifetime.</param>
    /// <returns>The editor's initial read.</returns>
    internal async Task OpenAsync(StudioPageClient api, InterfaceLanguage language, Func<Exception, string> describe, CancellationToken token)
    {
        Translate(language);
        Tool.ItemsSource = s_tools.Select(option => new Option(option.Id, option.Id + " · " + Text(option.Label))).ToArray();
        Level.ItemsSource = LevelChoices();
        Tool.SelectedIndex = Level.SelectedIndex = 0;
        AutomationProperties.SetName(Tool, Text("common.tool"));
        AutomationProperties.SetName(Level, Text("safety.verdict"));
        AutomationProperties.SetName(Pattern, Text("common.filter"));
        ToolTip.SetTip(ExternalHint, Text("safety.what_outside_text_covers"));
        OutsideText.IsVisible = false;
        var state = new JsonObject();
        var fallbackGroup = Guid.NewGuid().ToString("N");
        var closed = new HashSet<string>(StringComparer.Ordinal);
        var busy = false;
        var loading = false;

        ToggleAdd.Click += (_, _) => ShowAdd(!AddForm.IsVisible);
        Add.Click += async (_, _) => await AddAsync().ConfigureAwait(true);
        AddForm.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                ShowAdd(false);
                ToggleAdd.Focus();
            }
            else if (e.Key == Key.Enter && Pattern.IsFocused)
            {
                e.Handled = true;
                await AddAsync().ConfigureAwait(true);
            }
        };
        Tool.SelectionChanged += (_, _) => UpdatePreview();
        Level.SelectionChanged += (_, _) => UpdatePreview();
        Pattern.TextChanged += (_, _) => UpdatePreview();
        Search.TextChanged += (_, _) => RenderTable();
        External.IsCheckedChanged += async (_, _) =>
        {
            if (!busy && !loading)
            {
                var on = External.IsChecked == true;
                await SaveAsync(fields => fields["askOnExternalOrigin"] = on).ConfigureAwait(true);
            }
        };
        RevokeAll.Click += async (_, _) => await RunAsync(() => api.RevokePermissionAsync(string.Empty, false, token)).ConfigureAwait(true);
        ShowAdd(false);
        try
        {
            state = await api.PermissionsAsync(token).ConfigureAwait(true);
            Rules.IsVisible = OutsideText.IsVisible = true;
            Render();
        }
        catch (Exception)
        {
            ReadFailure.IsVisible = !token.IsCancellationRequested;
        }

        void ShowAdd(bool show)
        {
            AddForm.IsVisible = show;
            ToggleAdd.Content = Text(show ? "common.cancel" : "common.add_a_rule");
            if (show)
            {
                Tool.SelectedIndex = Level.SelectedIndex = 0;
                Pattern.Text = string.Empty;
                UpdatePreview();
            }
        }

        void UpdatePreview()
        {
            var tool = (Tool.SelectedItem as Option)?.Id ?? "bash";
            var pattern = Pattern.Text?.Trim() ?? string.Empty;
            Pattern.PlaceholderText = Text(tool == "bash" ? "safety.e_g_git_push_empty" : "safety.e_g_env_empty_the");
            Preview.Text = RuleText(tool, pattern);
            PreviewLevel.Text = (Level.SelectedItem as Option)?.Label;
            Explanation.Text = Explain(tool, pattern);
        }

        async Task AddAsync()
        {
            if (busy || Tool.SelectedItem is not Option tool || Level.SelectedItem is not Option level)
            {
                return;
            }

            await MoveAsync(RuleText(tool.Id, Pattern.Text?.Trim() ?? string.Empty), level.Id).ConfigureAwait(true);
            ShowAdd(false);
        }

        Task MoveAsync(string rule, string? target) => SaveAsync(fields =>
        {
            if (target is not null && Strings(fields[target]).Contains(rule, StringComparer.Ordinal))
            {
                return;
            }

            RemoveRules(fields, [rule]);
            if (target is not null)
            {
                fields[target]!.AsArray().Add(rule);
            }
        });

        Task SaveAsync(Action<JsonObject> change) => RunAsync(async () =>
        {
            var fresh = await api.PermissionsAsync(token).ConfigureAwait(true);
            var fields = new JsonObject { ["mode"] = fresh["mode"]?.DeepClone() };
            foreach (var (level, _, _) in s_levels)
            {
                fields[level] = fresh[level]?.DeepClone() ?? new JsonArray();
            }

            var before = fields.DeepClone();
            change(fields);
            return JsonNode.DeepEquals(before, fields) ? fresh : await api.SavePermissionsAsync(fields, token).ConfigureAwait(true);
        });

        async Task RunAsync(Func<Task<JsonObject>> write)
        {
            if (busy)
            {
                return;
            }

            busy = true;
            Rules.IsEnabled = OutsideText.IsEnabled = false;
            SaveNote.IsVisible = false;
            try
            {
                state = await write().ConfigureAwait(true);
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    var saved = error is KernelRefusal { Code: "runtime.rebuild_failed" or "runtime.saved_while_running" };
                    SaveTitle.Text = Text(saved ? "common.saved_not_applied_yet" : "common.that_did_not_go_through");
                    SaveTitle.Classes.Set("saved", saved);
                    SaveError.Text = describe(error);
                    SaveNote.IsVisible = true;
                    if (saved)
                    {
                        try
                        {
                            state = await api.PermissionsAsync(token).ConfigureAwait(true);
                        }
                        catch (Exception)
                        {
                            // Keep the save notice even if the subsequent refresh cannot be read.
                        }
                    }
                }
            }
            finally
            {
                busy = false;
                Rules.IsEnabled = OutsideText.IsEnabled = true;
                Render();
            }
        }

        void Render()
        {
            loading = true;
            External.IsChecked = (bool?)state["askOnExternalOrigin"] == true;
            loading = false;
            DrawWarnings();
            Recipes.Children.Clear();
            foreach (var recipe in s_recipes)
            {
                var toggle = new ToggleSwitch { Tag = recipe.Id, IsChecked = recipe.Rules.All(rule => Strings(state[recipe.Level]).Contains(rule, StringComparer.Ordinal)) };
                AutomationProperties.SetName(toggle, Text(recipe.Title));
                AutomationProperties.SetAutomationId(toggle, "action:permissions.recipe");
                Recipes.Children.Add(Line(Text(recipe.Title), Text(recipe.Hint), toggle, 10));
                toggle.IsCheckedChanged += async (_, _) =>
                {
                    var on = toggle.IsChecked == true;
                    await SaveAsync(fields =>
                    {
                        RemoveRules(fields, recipe.Rules);
                        if (on)
                        {
                            foreach (var rule in recipe.Rules)
                            {
                                fields[recipe.Level]!.AsArray().Add(rule);
                            }
                        }
                    }).ConfigureAwait(true);
                };
            }

            var mode = (string?)state["mode"] ?? "ask";
            Fallback.Children.Clear();
            foreach (var (id, label, hint) in s_modes)
            {
                var button = new RadioButton
                {
                    Content = Text(label),
                    Tag = id,
                    Classes = { "segmented" },
                    GroupName = fallbackGroup,
                    IsChecked = mode == id,
                };
                if (button.IsChecked == true)
                {
                    FallbackHint.Text = Text(hint);
                }

                AutomationProperties.SetAutomationId(button, "action:permissions.mode");
                Fallback.Children.Add(button);
                button.IsCheckedChanged += async (_, _) =>
                {
                    if (button.IsChecked == true && mode != id)
                    {
                        await SaveAsync(fields => fields["mode"] = id).ConfigureAwait(true);
                    }
                };
            }

            DrawGrants();
            RenderTable();
        }

        void DrawWarnings()
        {
            Warnings.Children.Clear();
            if ((string?)state["shadowedBy"] is { Length: > 0 } project)
            {
                Warning(Text("safety.this_project_ships_its_own"), Text("safety.path_declares_permissions_too_and", ("path", project)));
            }

            if (state["dormant"] is JsonArray { Count: > 0 } dormant)
            {
                Warning(Text("safety.n_rule_s_name_no", ("n", Count(dormant.Count))), Text("safety.a_shell_command_is_written"));
            }

            if ((string?)state["rememberedErrorCode"] is { Length: > 0 })
            {
                Warning(
                    Text("safety.could_not_read_remembered_permission"),
                    Text("safety.check_or_repair_the_grant") + " " + (string?)state["rememberedPath"]);
            }

            void Warning(string title, string text)
            {
                var content = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = title, FontWeight = FontWeight.Medium, Classes = { "permission-detail" } },
                        new TextBlock { Text = text, Classes = { "permission-detail" } },
                    },
                };
                Warnings.Children.Add(new Border
                {
                    Child = content,
                    Padding = new Thickness(10, 6),
                    Margin = new Thickness(0, 0, 0, 6),
                    CornerRadius = new CornerRadius(7),
                    [!Border.BackgroundProperty] = StudioTypography.Resource("WarnWashBrush"),
                });
            }
        }

        void DrawGrants()
        {
            RememberedRows.Children.Clear();
            var stored = Strings(state["remembered"]).ToArray();
            Remembered.IsVisible = stored.Length > 0;
            RememberedPath.Text = (string?)state["rememberedPath"];
            foreach (var rule in stored)
            {
                RememberedRows.Children.Add(Grant(rule, true));
            }

            GrantRows.Children.Clear();
            var granted = Strings(state["granted"]).ToArray();
            SessionGrants.IsVisible = granted.Length > 0;
            GrantTitle.Text = Text("safety.n_more_allowed_for_this", ("n", Count(granted.Length)));
            foreach (var rule in granted)
            {
                GrantRows.Children.Add(Grant(rule, false));
            }

            Border Grant(string rule, bool remembered)
            {
                var revoke = new Button { Content = Text("safety.take_back"), Tag = rule, Classes = { "preset-act" } };
                var action = remembered ? "action:permissions.revoke-remembered" : "action:permissions.revoke-session";
                AutomationProperties.SetAutomationId(revoke, action);
                AutomationProperties.SetName(revoke, Text("safety.take_back_rule", ("rule", rule)));
                revoke.Click += async (_, _) => await RunAsync(() => api.RevokePermissionAsync(rule, remembered, token)).ConfigureAwait(true);
                return Line(rule, string.Empty, revoke, 6, monospace: true);
            }
        }

        void RenderTable()
        {
            var lists = s_levels.SelectMany(level => Strings(state[level.Id]).Select(value => Rule.From(value, level.Id))).ToArray();
            var total = lists.Length;
            Total.Text = total == 0 ? Text("safety.no_rules_of_your_own") : Text("common.n_rules", ("n", Count(total)));
            CountDeny.IsVisible = CountAsk.IsVisible = CountAllow.IsVisible = total > 0;
            Deny.Text = Count(lists.Count(row => row.Level == "deny"));
            Ask.Text = Count(lists.Count(row => row.Level == "ask"));
            Allow.Text = Count(lists.Count(row => row.Level == "allow"));
            Search.IsVisible = total > 6;
            RulesPath.Text = total > 0 ? (string?)state["path"] : string.Empty;
            RulesPath.IsVisible = total > 0;
            var filter = Search.IsVisible ? Search.Text?.Trim() ?? string.Empty : string.Empty;
            var dormant = (state["dormant"] as JsonArray)?.OfType<JsonObject>()
                .Select(item => ((string?)item["list"], (string?)item["rule"])).ToHashSet() ?? [];
            RuleTables.Children.Clear();
            foreach (var group in lists.Where(row => row.Raw.Contains(filter, StringComparison.OrdinalIgnoreCase)).GroupBy(row => row.Tool)
                .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.CurrentCulture))
            {
                var rows = group.OrderBy(row => Array.FindIndex(s_levels, level => level.Id == row.Level))
                    .ThenBy(row => row.Pattern, StringComparer.CurrentCulture).ToArray();
                var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
                var name = new WrapPanel { ItemSpacing = 8, LineSpacing = 4 };
                var expanded = !closed.Contains(group.Key);
                var caret = new Avalonia.Controls.Shapes.Path
                {
                    Data = Geometry.Parse("M8 5l7 7-7 7z"),
                    Width = 8,
                    Height = 8,
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center,
                    RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                    RenderTransform = new RotateTransform(expanded ? 90 : 0),
                };
                caret.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, caret.GetResourceObservable("GhostBrush"));
                name.Children.Add(caret);
                name.Children.Add(new TextBlock
                {
                    Text = group.Key,
                    VerticalAlignment = VerticalAlignment.Center,
                    [!TextBlock.FontFamilyProperty] = StudioTypography.Resource("WorkbenchMonoFamily"),
                });
                var toolName = s_tools.FirstOrDefault(tool => tool.Id == group.Key).Label;
                if (toolName is not null)
                {
                    name.Children.Add(new TextBlock { Text = Text(toolName), Classes = { "permission-detail" }, VerticalAlignment = VerticalAlignment.Center });
                }

                if (rows.Any(row => !dormant.Contains((row.Level, row.Raw))))
                {
                    name.Children.Add(new TextBlock
                    {
                        Text = Text(Matching(group.Key)),
                        Classes = { "permission-detail" },
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                }

                heading.Children.Add(name);
                var count = new TextBlock { Text = Text("common.n_2", ("n", Count(rows.Length))), Classes = { "permission-detail" }, VerticalAlignment = VerticalAlignment.Center };
                heading.Children.Add(count);
                Grid.SetColumn(count, 1);
                var header = new Button { Content = heading, Tag = group.Key, Classes = { "preset-flat", "permission-group" } };
                AutomationProperties.SetAutomationId(header, "action:permissions.rule-group");
                var body = new StackPanel { IsVisible = expanded };
                header.Click += (_, _) =>
                {
                    if (!closed.Add(group.Key))
                    {
                        closed.Remove(group.Key);
                    }

                    body.IsVisible = !closed.Contains(group.Key);
                    caret.RenderTransform = new RotateTransform(body.IsVisible ? 90 : 0);
                };
                RuleTables.Children.Add(new Border { Child = header, Classes = { "permission-line" } });
                foreach (var row in rows)
                {
                    AddRuleRow(row, dormant.Contains((row.Level, row.Raw)), body);
                }

                RuleTables.Children.Add(body);
            }

            if (total > 0 && RuleTables.Children.Count == 0)
            {
                RuleTables.Children.Add(Caption(Text("safety.no_rule_matches_that")));
            }
        }

        void AddRuleRow(Rule rule, bool dormant, StackPanel body)
        {
            var verdicts = LevelChoices();
            var verdict = new ComboBox
            {
                ItemsSource = verdicts,
                SelectedItem = verdicts.Single(item => item.Id == rule.Level),
                Tag = rule.Raw,
                Width = 140,
                Classes = { "agent-select", "settings-input", "permission-verdict" },
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetAutomationId(verdict, "action:permissions.rule-level");
            AutomationProperties.SetName(verdict, Text("safety.what_happens_on_rule", ("rule", rule.Raw)));
            verdict.SelectionChanged += async (_, _) =>
            {
                if (verdict.SelectedItem is Option target && target.Id != rule.Level)
                {
                    await MoveAsync(rule.Raw, target.Id).ConfigureAwait(true);
                }
            };
            var remove = new Button { Content = Text("common.delete"), Tag = rule.Raw, Classes = { "preset-flat", "preset-muted", "permission-ghost" } };
            AutomationProperties.SetAutomationId(remove, "action:permissions.remove-rule");
            AutomationProperties.SetName(remove, Text("safety.delete_rule", ("rule", rule.Raw)));
            remove.Click += async (_, _) => await MoveAsync(rule.Raw, null).ConfigureAwait(true);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { verdict, remove } };
            var hint = dormant ? Text("safety.no_tool_is_named_tool", ("tool", rule.Tool)) : Unusual(rule.Tool, rule.Pattern);
            var element = Line(rule.Pattern.Length == 0 ? Text("safety.the_whole_tool") : rule.Pattern, hint, actions, 5, monospace: true);
            var dot = new Avalonia.Controls.Shapes.Ellipse
            {
                Width = 6,
                Height = 6,
                VerticalAlignment = VerticalAlignment.Center,
                [!Avalonia.Controls.Shapes.Ellipse.FillProperty] = StudioTypography.Resource(s_levels.Single(item => item.Id == rule.Level).Brush),
            };
            var row = new Border
            {
                BorderThickness = new Thickness(2, 0, 0, 0),
                Padding = new Thickness(10, 0, 0, 0),
                Child = new Grid { ColumnDefinitions = new ColumnDefinitions("18,*"), Children = { dot, element } },
            };
            row.Bind(Border.BorderBrushProperty, row.GetResourceObservable("WarnBrush"));
            Grid.SetColumn(element, 1);
            body.Children.Add(row);
        }
    }

    /// <summary>Removes exact matches without rewriting unrelated rules.</summary>
    /// <param name="fields">The editable record.</param>
    /// <param name="rules">The identities to remove.</param>
    private static void RemoveRules(JsonObject fields, IReadOnlyCollection<string> rules)
    {
        foreach (var (level, _, _) in s_levels)
        {
            fields[level] = new JsonArray(Strings(fields[level]).Where(rule => !rules.Contains(rule)).Select(rule => (JsonNode?)JsonValue.Create(rule)).ToArray());
        }
    }

    /// <summary>Reads a list of rule strings.</summary>
    /// <param name="list">The rules on the wire.</param>
    /// <returns>The rule IDs without any transformation.</returns>
    private static IEnumerable<string> Strings(JsonNode? list) => (list as JsonArray)?.Select(item => (string?)item).OfType<string>() ?? [];

    /// <summary>Formats a rule count for a message parameter.</summary>
    /// <param name="number">The rule count.</param>
    /// <returns>The localized number.</returns>
    private static string Count(int number) => number.ToString(CultureInfo.CurrentCulture);

    /// <summary>Formats a rule exactly as the kernel's settings editor expects it.</summary>
    /// <param name="tool">The selected canonical tool.</param>
    /// <param name="pattern">The optional subject entered by the user.</param>
    /// <returns>The rule text.</returns>
    private static string RuleText(string tool, string pattern) => pattern.Length > 0 ? tool + "(" + pattern + ")" : tool;

    /// <summary>Lays out an approval or recipe row.</summary>
    /// <param name="title">The visible label.</param>
    /// <param name="hint">Optional explanatory text.</param>
    /// <param name="action">The row action.</param>
    /// <param name="vertical">Vertical padding.</param>
    /// <param name="monospace">Whether the title is a rule pattern.</param>
    /// <returns>The mounted row.</returns>
    private static Border Line(string title, string hint, Control action, double vertical, bool monospace = false)
    {
        var label = new TextBlock { Text = title, LineHeight = 19, TextWrapping = TextWrapping.Wrap };
        if (monospace)
        {
            label[!TextBlock.FontFamilyProperty] = StudioTypography.Resource("WorkbenchMonoFamily");
        }

        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { label } };
        if (!string.IsNullOrEmpty(hint))
        {
            text.Children.Add(new TextBlock { Text = hint, Classes = { "permission-detail" } });
        }

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12, Children = { text, action } };
        action.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(action, 1);
        return new Border { Child = row, Classes = { "permission-line" }, Padding = new Thickness(12, vertical) };
    }

    /// <summary>Identifies the matching surface stated at a tool group's head.</summary>
    /// <param name="tool">The canonical rule tool.</param>
    /// <returns>The matching hint key.</returns>
    private static string Matching(string tool) => tool switch
    {
        "bash" => "safety.matched_by_command_words",
        "grep" or "glob" => "safety.matched_against_the_search_pattern",
        "web_fetch" => "safety.matched_against_the_url",
        _ => "safety.matched_by_path_crosses",
    };

    /// <summary>Provides the verdict chooser's localized options.</summary>
    /// <returns>The three canonical alternatives.</returns>
    private Option[] LevelChoices() => s_levels.Select(item => new Option(item.Id, Text(item.Label))).ToArray();

    /// <summary>Describes the pattern as it was entered in the form.</summary>
    /// <param name="tool">The chosen tool.</param>
    /// <param name="pattern">The entered subject.</param>
    /// <returns>The preview explanation.</returns>
    private string Explain(string tool, string pattern)
    {
        if (pattern.Length == 0)
        {
            return Text("safety.every_call_to_this_tool");
        }

        if (tool == "bash")
        {
            if (pattern.EndsWith(":*", StringComparison.Ordinal))
            {
                return Text("safety.commands_starting_with_cmd_whatever", ("cmd", pattern[..^2]));
            }

            return pattern.Contains('*')
                ? Text("safety.commands_matching_pat_as_a", ("pat", pattern))
                : Text("safety.exactly_the_command_cmd", ("cmd", pattern));
        }

        return pattern.Contains('*')
            ? Text("safety.calls_whose_path_matches_pat", ("pat", pattern))
            : Text("safety.exactly_the_path_path", ("path", pattern));
    }

    /// <summary>Explains unusual rules without repeating the common rule shape.</summary>
    /// <param name="tool">The tool carrying the rule.</param>
    /// <param name="pattern">The saved subject.</param>
    /// <returns>Optional text for the rule row.</returns>
    private string Unusual(string tool, string pattern)
    {
        if (pattern.Length == 0)
        {
            return Text("safety.every_call_to_this_tool");
        }

        if (tool == "bash")
        {
            if (pattern.EndsWith(":*", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return Text(pattern.Contains('*') ? "safety.matches_the_whole_command_not" : "safety.exactly_this_command_one_more");
        }

        if (pattern.Contains('*'))
        {
            return string.Empty;
        }

        return Text(tool is "grep" or "glob" or "web_fetch" or "Browser" or "Computer" ? "safety.exactly_this_one_value" : "safety.exactly_this_one_path");
    }

    /// <summary>A reference rule recipe.</summary>
    /// <param name="Id">The stable action target.</param>
    /// <param name="Title">The visible action title.</param>
    /// <param name="Hint">The visible description.</param>
    /// <param name="Level">The requested verdict.</param>
    /// <param name="Rules">The exact rules in the recipe.</param>
    private sealed record Recipe(string Id, string Title, string Hint, string Level, string[] Rules);

    /// <summary>A raw saved rule opened into its tool and optional subject for the table.</summary>
    /// <param name="Raw">The unchanged persistence identity.</param>
    /// <param name="Tool">The displayed group.</param>
    /// <param name="Pattern">The optional subject text.</param>
    /// <param name="Level">The current verdict.</param>
    private sealed record Rule(string Raw, string Tool, string Pattern, string Level)
    {
        /// <summary>Parses only the outer formatting of the rule for display.</summary>
        /// <param name="raw">The retained wire value.</param>
        /// <param name="level">The containing verdict list.</param>
        /// <returns>The display representation.</returns>
        public static Rule From(string raw, string level)
        {
            var at = raw.IndexOf('(');
            return at >= 0 && raw.EndsWith(')')
                ? new Rule(raw, raw[..at].Trim(), raw[(at + 1)..^1], level)
                : new Rule(raw, raw.Trim(), string.Empty, level);
        }
    }
}
