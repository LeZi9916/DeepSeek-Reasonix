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
}
