using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Shows the reviewed contribution and the kernel's declared risks without rendering the wire payload.</summary>
internal sealed partial class MarketPlanCandidate : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="MarketPlanCandidate"/> class.</summary>
    public MarketPlanCandidate()
    {
        InitializeComponent();
        ToggleReasons.Click += (_, _) => Reasons.IsVisible = !Reasons.IsVisible;
    }

    /// <summary>Projects a single reviewed action.</summary>
    /// <param name="action">Structured action advertised by the kernel.</param>
    /// <param name="language">Settings language.</param>
    internal void Open(JsonObject action, InterfaceLanguage language)
    {
        var kind = (string?)action["kind"] ?? string.Empty;
        CandidateName.Text = (string?)action["name"] ?? kind;
        CandidateMetadata.Text = Join((string?)action["version"], (string?)action["manifestKind"], (string?)action["transport"]);
        CandidateMetadata.IsVisible = !string.IsNullOrEmpty(CandidateMetadata.Text);
        Tag = CandidateName.Text;
        AutomationProperties.SetName(this, CandidateName.Text);

        if (kind == "plugin" && (string?)action["commit"] is { Length: > 0 } commit)
        {
            var row = Fact(Text("ext.resolved_commit"), commit);
            var copy = new Button
            {
                Content = Text("common.copy"),
                Classes = { "market-detail-action" },
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetName(copy, Text("ext.copy_commit"));
            copy.Click += async (_, _) =>
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                {
                    await clipboard.SetTextAsync(commit).ConfigureAwait(true);
                }
            };
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Grid.SetColumn(copy, 2);
            row.Children.Add(copy);
            RiskRows.Children.Add(row);
        }

        if (action["runtime"] is JsonObject runtime)
        {
            Execution("ext.long_running_process", Command(runtime), "ext.runs_inside_reasonix_can_read");
            if (runtime["tools"] is JsonArray { Count: > 0 } tools)
            {
                Execution("ext.new_tools", string.Join(language == InterfaceLanguage.Zh ? "、" : ", ", tools.Select(item => (string?)item)), "ext.the_agent_can_call_them");
            }
        }

        if ((int?)action["hookCount"] is > 0 and int hooks)
        {
            var number = hooks.ToString(CultureInfo.CurrentCulture);
            var label = language == InterfaceLanguage.Zh
                ? MessageCatalog.Get("common.n_2", language, ("n", number))
                : MessageCatalog.Get(hooks == 1 ? "ext.n_1_hook" : "ext.n_hooks", language, ("n", number));
            Execution("ext.automatic_hooks", label, "ext.run_automatically_during_the_session");
        }

        if ((int?)action["toolCount"] is > 0 and int servers)
        {
            Execution("ext.external_services", Count(servers, "ext.n_1_mcp_service", "ext.n_mcp_services"), "ext.provides_capabilities_equivalent_to_the");
        }

        if (kind == "mcp")
        {
            if ((string?)action["command"] is { Length: > 0 })
            {
                Execution("ext.will_start", Command(action));
            }

            if ((string?)action["url"] is { Length: > 0 } url)
            {
                Execution("ext.will_connect_to", url);
            }
        }

        if (action["mod"] is JsonObject mod)
        {
            ShowMod(mod, language);
        }

        var adds = new List<string>();
        CountContribution("skillCount", "ext.n_1_skill", "ext.n_skills");
        CountContribution("commandCount", "ext.n_1_command", "ext.n_commands");
        CountContribution("agentCount", "ext.n_1_subagent", "subagent.group.count");
        CountContribution("promptCount", "ext.n_1_prompt", "ext.n_prompts");
        CountContribution("themeCount", "ext.n_1_palette", "ext.n_palettes");
        if (adds.Count > 0)
        {
            RiskRows.Children.Add(Fact(Text("ext.will_be_added"), Join(adds.ToArray())));
        }

        if (action["env"] is JsonObject variables)
        {
            foreach (var pair in variables)
            {
                RiskRows.Children.Add(Fact(Text("ext.needs_filling_in"), pair.Key));
            }
        }

        foreach (var item in action["skippedCapabilities"]?.AsArray().OfType<JsonObject>() ?? [])
        {
            RiskRows.Children.Add(Fact(Text("ext.unusable"), Join((string?)item["capability"], (string?)item["reason"])));
        }

        if ((bool?)action["previewTruncated"] == true)
        {
            RiskRows.Children.Add(Fact(Text("ext.shortened"), Text("ext.some_text")));
        }

        var reasons = action["riskReasons"]?.AsArray().Select(item => (string?)item).OfType<string>().ToArray() ?? [];
        ToggleReasons.IsVisible = reasons.Length > 0;
        ToggleReasons.Content = MessageCatalog.Get("ext.the_kernels_assessment_n", language, ("n", reasons.Length.ToString(CultureInfo.CurrentCulture)));
        foreach (var reason in reasons)
        {
            Reasons.Children.Add(new TextBlock { Text = reason, Classes = { "market-detail-hint" }, TextWrapping = TextWrapping.Wrap });
        }

        string Text(string key) => MessageCatalog.Get(key, language);

        string Count(int number, string singular, string plural) =>
            MessageCatalog.Get(language != InterfaceLanguage.Zh && number == 1 ? singular : plural, language, ("n", number.ToString(CultureInfo.CurrentCulture)));

        void CountContribution(string field, string singular, string plural)
        {
            if ((int?)action[field] is > 0 and int count)
            {
                adds.Add(Count(count, singular, plural));
            }
        }

        void Execution(string key, string value, string? hint = null)
        {
            var row = Fact(Text(key), hint is null ? value : Join(value, Text(hint)));
            row.Classes.Add("execution");
            RiskRows.Children.Add(row);
        }
    }

    /// <summary>Joins optional display facts without creating empty separators.</summary>
    /// <param name="parts">Display values.</param>
    /// <returns>The compact summary.</returns>
    private static string Join(params string?[] parts) => string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));

    /// <summary>Formats the executable and argument preview supplied by the host.</summary>
    /// <param name="source">An action or its runtime declaration.</param>
    /// <returns>The display command.</returns>
    private static string Command(JsonObject source) => string.Join(" ", new[] { (string?)source["command"] }
        .Concat(source["args"]?.AsArray().Select(item => (string?)item) ?? []).Where(value => !string.IsNullOrWhiteSpace(value)));

    /// <summary>Displays one declared risk or requirement.</summary>
    /// <param name="label">The localized fact label.</param>
    /// <param name="value">The preview text.</param>
    /// <returns>The fact row.</returns>
    private static Grid Fact(string label, string value)
    {
        var heading = new TextBlock { Text = label, Classes = { "market-detail-hint" }, MaxWidth = 130, TextWrapping = TextWrapping.Wrap };
        var content = new SelectableTextBlock { Text = value, Classes = { "market-detail-hint" }, TextWrapping = TextWrapping.Wrap };
        content.Bind(TextBlock.FontFamilyProperty, content.GetResourceObservable("WorkbenchMonoFamily"));
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 7, Margin = new Thickness(0, 4, 0, 0) };
        Grid.SetColumn(content, 1);
        row.Children.Add(heading);
        row.Children.Add(content);
        return row;
    }
}
