using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.State.Transcript;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Reasonix.Studio.Pages;

internal sealed partial class ActivityDisclosure
{
    /// <summary>Argument keys that describe a call to the reader.</summary>
    private static readonly string[] s_args = ["command", "path", "file_path", "pattern", "query", "url", "description", "prompt", "step_id", "name"];

    /// <summary>Refreshes the elapsed seconds shown by a folded running group.</summary>
    private readonly DispatcherTimer _liveClock = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>The live call's caption, start timestamp and language.</summary>
    private (string Caption, long Since, InterfaceLanguage Language)? _live;

    /// <summary>Extracts and compacts the user-facing part of a structured argument.</summary>
    /// <param name="raw">The tool's canonical argument payload.</param>
    /// <returns>The clipped argument description.</returns>
    internal static string ShortArgument(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        string value;
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            value = Pick(root);
            if (value.Length == 0 && root.ValueKind == JsonValueKind.Object && root.TryGetProperty("arguments", out var nested))
            {
                value = Pick(nested);
            }
        }
        catch (JsonException)
        {
            value = raw.TrimStart().StartsWith('{') ? string.Empty : raw;
        }

        value = Regex.Replace(value, @"\s+", " ");
        return value[..Math.Min(96, value.Length)];

        static string Pick(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            return s_args.Select(key => element.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null)
                .FirstOrDefault(value => !string.IsNullOrEmpty(value)) ?? string.Empty;
        }
    }

    /// <summary>Configures the visible current step and the running count of a tool group.</summary>
    /// <param name="tool">The last currently running call.</param>
    /// <param name="running">Number of running calls in this group.</param>
    /// <param name="failed">Whether any call has failed.</param>
    /// <param name="language">The transcript's language.</param>
    internal void ConfigureLive(ToolItem? tool, int running, bool failed, InterfaceLanguage language)
    {
        var badge = this.FindControl<TextBlock>("RunningCount")!;
        badge.Text = running > 0 ? MessageCatalog.Get("chat.n_running", language, ("n", Format.Count(running, language))) : null;
        this.FindControl<Viewbox>("StatusMark")!.IsVisible = running == 0 && !failed;
        this.FindControl<Button>("Toggle")!.Classes.Set("activity-failed", failed);
        var title = this.FindControl<TextBlock>("TitleLabel")!;
        title.IsVisible = title.Text is not null && (_body.Expanded || failed);

        if (tool is not null)
        {
            var name = string.IsNullOrEmpty(tool.Call.ResolvedName) ? tool.Call.Name : tool.Call.ResolvedName;
            var key = LiveVerbOf(name);
            var verb = key is null ? name : MessageCatalog.Get(key, language);
            var argument = ShortArgument(tool.Call.Args);
            var caption = argument.Length > 0 ? verb + " " + argument : verb;
            var since = tool.Call.StartedAt > 0 ? tool.Call.StartedAt : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _live = (caption, since, language);
        }

        RefreshLiveHeading();
    }

    /// <summary>Returns the reference caption key for a known activity.</summary>
    /// <param name="name">The resolved call name.</param>
    /// <returns>The caption key, or null to use the name itself.</returns>
    private static string? LiveVerbOf(string name)
    {
        if (name.StartsWith("edit", StringComparison.Ordinal) || name.StartsWith("write", StringComparison.Ordinal) || name.StartsWith("multi", StringComparison.Ordinal))
        {
            return "common.edit";
        }

        if (name.StartsWith("mcp__", StringComparison.Ordinal))
        {
            return "settings.external_tools";
        }

        if (name.StartsWith("bash_", StringComparison.Ordinal))
        {
            return "chat.run_command";
        }

        return name switch
        {
            "web_search" or "web_fetch" => "chat.network",
            "task" or "fleet" or "read_only_task" or "read_subagent_result" or "complete_subtask" or "parallel_tasks" or "run_skill" or "read_only_skill" or "list_subagents" => "chat.subtask",
            "delete_range" or "delete_symbol" or "move_file" or "notebook_edit" or "install_skill" or "install_source" => "common.edit",
            "use_capability" => "settings.external_tools",
            "bash" or "kill_shell" => "chat.run_command",
            "grep" or "glob" => "chat.search",
            "read_file" or "ls" or "code_index" or "read_skill" or "lsp_diagnostics" or "lsp_definition" or "lsp_references" or "lsp_hover"
                or "history" or "list_sessions" or "read_session" or "docs" or "context_budget" or "recall" or "memory" => "chat.read",
            _ => null,
        };
    }

    /// <summary>Projects the running caption into the folded heading.</summary>
    private void RefreshLiveHeading()
    {
        var now = this.FindControl<SweepTextBlock>("ActivityNow")!;
        var badge = this.FindControl<TextBlock>("RunningCount")!;
        var summary = this.FindControl<StackPanel>("SummaryContent")!;
        now.IsVisible = !_body.Expanded && _live is not null;
        badge.IsVisible = _body.Expanded && badge.Text is not null;
        Grid.SetColumn(summary, now.IsVisible ? 5 : 4);
        if (now.IsVisible && this.IsAttachedToVisualTree())
        {
            UpdateLiveCaption();
            _liveClock.Start();
        }
        else
        {
            _liveClock.Stop();
        }
    }

    /// <summary>Updates the visible running caption from the call's start time.</summary>
    private void UpdateLiveCaption()
    {
        if (IsEffectivelyVisible && _live is { } live)
        {
            var seconds = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - live.Since) / 1000;
            var suffix = MessageCatalog.Get("common.n_s", live.Language, ("n", Format.Count(seconds, live.Language)));
            this.FindControl<SweepTextBlock>("ActivityNow")!.Text = live.Caption + " · " + suffix;
        }
    }
}
