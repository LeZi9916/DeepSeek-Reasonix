using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Renders the configured presets and the capabilities each definition selects.</summary>
    /// <param name="card">The persona and presets card.</param>
    /// <param name="catalog">The kernel's preset inventory.</param>
    private void ShowPresetCatalog(StackPanel card, JsonObject catalog)
    {
        var presets = new StackPanel { Tag = "persona.agent_presets", Spacing = 9, Margin = new Thickness(0, 12) };
        card.Children.Add(presets);
        presets.Children.Add(new TextBlock { Text = MessageCatalog.Get("persona.agent_presets", Language), FontSize = 16, FontWeight = FontWeight.SemiBold });
        presets.Children.Add(Detail(MessageCatalog.Get("persona.presets_define_which_capabilities_are", Language)));
        var tiles = new WrapPanel();
        presets.Children.Add(tiles);
        foreach (var entry in catalog["presets"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            if (entry["definition"] is not JsonObject definition || (string?)definition["id"] is not string id)
            {
                continue;
            }

            var builtin = id.StartsWith("builtin:", StringComparison.Ordinal);
            string Copy(string field) => builtin ? MessageCatalog.Get((string?)definition[field] ?? string.Empty, Language) : (string?)definition[field] ?? string.Empty;
            var heading = new StackPanel { Spacing = 4 };
            heading.Children.Add(new TextBlock { Text = Copy("name"), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            heading.Children.Add(Detail(Copy("description")));
            var details = new StackPanel { Spacing = 5 };
            foreach (var (kind, label) in new[] { ("tools", "common.tool"), ("skills", "settings.skills"), ("mcp", "persona.mcp_servers"), ("extensions", "ext.extension"), ("hooks", "persona.automation_hooks"), ("commands", "persona.slash_commands") })
            {
                if (definition[kind] is not JsonObject selection)
                {
                    continue;
                }

                var members = selection["members"]?.AsArray().Select(m => (string?)m?["id"]);
                var summary = (string?)selection["mode"] == "inherit"
                    ? MessageCatalog.Get("persona.inherit_current_configuration", Language)
                    : string.Join(", ", members ?? []);
                details.Children.Add(Detail(MessageCatalog.Get(label, Language) + " · " + (summary.Length == 0 ? "0" : summary)));
            }

            var tile = new Expander { Header = heading, Content = details, Width = 245, Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Top };
            AutomationProperties.SetAutomationId(tile, "action:agent-preset.view");
            AutomationProperties.SetName(tile, Copy("name"));
            tiles.Children.Add(tile);
        }

        foreach (var issue in catalog["diagnostics"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            presets.Children.Add(Detail((string?)issue["detail"] ?? string.Empty));
        }
    }

    /// <summary>Builds a searchable catalog with category counts and entry details.</summary>
    /// <param name="card">The data settings card.</param>
    /// <param name="catalog">Entries and per-file read errors from the current machine.</param>
    private void ShowMemoryCatalog(StackPanel card, JsonObject catalog)
    {
        var entries = (catalog["memories"]?.AsArray().OfType<JsonObject>() ?? [])
            .OrderByDescending(m => (string?)m["lastUsedAt"] ?? (string?)m["updatedAt"] ?? (string?)m["createdAt"], StringComparer.Ordinal)
            .ThenBy(m => (string?)m["title"] ?? (string?)m["name"], StringComparer.CurrentCulture).ToArray();
        card.Children.Add(Detail(MessageCatalog.Get("settings.what_it_remembers", Language)));
        foreach (var failure in catalog["unreadable"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            card.Children.Add(Detail((string?)failure["name"] + " · " + (string?)failure["error"]));
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("142,*"), ColumnSpacing = 12, Margin = new Thickness(0, 10), Tag = "data.memory" };
        AutomationProperties.SetAutomationId(grid, "class:memgrid");
        card.Children.Add(grid);
        var rail = new StackPanel { Spacing = 2 };
        var body = new StackPanel { Spacing = 5 };
        Grid.SetColumn(body, 1);
        grid.Children.Add(rail);
        grid.Children.Add(body);
        var search = new TextBox { PlaceholderText = MessageCatalog.Get("memory.search_memory", Language) };
        AutomationProperties.SetAutomationId(search, "action:memory.search");
        AutomationProperties.SetName(search, search.PlaceholderText);
        body.Children.Add(search);
        var rows = new StackPanel { Spacing = 2 };
        body.Children.Add(rows);
        Func<JsonObject, bool> matches = _ => true;
        var shown = 30;
        var options = new List<(string Label, Func<JsonObject, bool> Matches)>
        {
            (MessageCatalog.Get("common.all", Language), _ => true),
            (MessageCatalog.Get("memory.new", Language), m => (bool?)m["new"] == true),
        };
        foreach (var (kind, label) in new[] { ("constraint", "memory.constraints"), ("user", "remote.user"), ("feedback", "feedback.feedback"), ("project", "common.project"), ("reference", "memory.reference") })
        {
            options.Add((MessageCatalog.Get(label, Language), m => (string?)m["type"] == kind));
        }

        foreach (var galaxy in entries.Select(m => (string?)m["galaxy"]).Where(g => !string.IsNullOrEmpty(g)).Distinct().OrderBy(g => g))
        {
            options.Add((galaxy!, m => (string?)m["galaxy"] == galaxy));
        }

        options.Add((MessageCatalog.Get("memory.faded", Language), m => (string?)m["state"] == "faded" && (bool?)m["noDecay"] != true));
        foreach (var option in options)
        {
            var count = entries.Count(option.Matches);
            var button = new RadioButton { Content = option.Label + "  " + count, IsChecked = rail.Children.Count == 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            button.Classes.Add("segmented");
            AutomationProperties.SetAutomationId(button, "action:memory.filter");
            button.IsCheckedChanged += (_, _) =>
            {
                if (button.IsChecked == true)
                {
                    matches = option.Matches;
                    shown = 30;
                    Render();
                }
            };
            rail.Children.Add(button);
        }

        search.TextChanged += (_, _) =>
        {
            shown = 30;
            Render();
        };
        Render();

        void Render()
        {
            rows.Children.Clear();
            var terms = (search.Text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var list = entries.Where(matches).Where(m =>
            {
                var haystack = string.Join("\n", new[] { "name", "title", "description", "body", "galaxy" }.Select(key => (string?)m[key]));
                return terms.All(term => haystack.Contains(term, StringComparison.CurrentCultureIgnoreCase));
            }).ToArray();
            foreach (var item in list.Take(shown))
            {
                var title = (string?)item["title"] ?? (string?)item["name"] ?? string.Empty;
                var detail = new StackPanel { Spacing = 5 };
                detail.Children.Add(Detail((string?)item["description"] ?? string.Empty));
                detail.Children.Add(new SelectableTextBlock { Text = (string?)item["body"], TextWrapping = TextWrapping.Wrap });
                var open = new Expander { Header = title, Content = detail, IsExpanded = rows.Children.Count == 0, HorizontalAlignment = HorizontalAlignment.Stretch };
                AutomationProperties.SetAutomationId(open, "action:memory.open");
                rows.Children.Add(open);
            }

            if (list.Length == 0)
            {
                rows.Children.Add(Detail(MessageCatalog.Get(entries.Length == 0 ? "memory.no_entries_yet" : "memory.no_memories_match", Language)));
            }
            else if (shown < list.Length)
            {
                var more = new Button { Content = MessageCatalog.Get("memory.show_n_more_total_in", Language, ("n", Math.Min(30, list.Length - shown).ToString(System.Globalization.CultureInfo.CurrentCulture)), ("total", list.Length.ToString(System.Globalization.CultureInfo.CurrentCulture))) };
                more.Click += (_, _) =>
                {
                    shown += 30;
                    Render();
                };
                rows.Children.Add(more);
            }
        }
    }
}
