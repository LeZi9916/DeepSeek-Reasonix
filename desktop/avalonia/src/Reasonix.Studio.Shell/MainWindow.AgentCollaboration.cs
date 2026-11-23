using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Settings;
using System;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Mounts the collaboration preferences under Agent behavior.</summary>
    /// <param name="column">The current settings page.</param>
    /// <param name="api">The active kernel.</param>
    /// <returns>The initial reads for the three settings.</returns>
    private async Task AgentCollaborationAsync(StackPanel column, StudioPageClient api)
    {
        var card = Card(column, "settings.collaboration");
        var referenceRows = new StackPanel();
        var claimRows = new StackPanel();
        var peerSettings = new PeerSettingsView { Tag = "remote.messages_between_sessions_experimental" };
        card.Children.Add(referenceRows);
        card.Children.Add(claimRows);
        card.Children.Add(peerSettings);
        await Task.WhenAll(
            ReadReferenceAsync(),
            ReadClaimsAsync(),
            peerSettings.OpenAsync(
                api,
                Language,
                token => _engine!.TreeAsync(new TreeWindow { Limit = 1 }, token),
                DescribeError,
                _closing.Token)).ConfigureAwait(true);

        async Task ReadReferenceAsync()
        {
            var state = (await ReadAgentPreferenceAsync(api.SessionReferenceAsync).ConfigureAwait(true)).Setting;
            if (state is null)
            {
                referenceRows.Children.Add(Detail(Message("composer.the_reference_settings_could_not")));
                return;
            }

            var busy = false;
            var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var tokens = new SettingsUnitField { Width = 108 };
            var reset = new Button { Content = Message("composer.restore_default"), Classes = { "preset-act" }, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetAutomationId(reset, "action:session-ref.reset");
            controls.Children.Add(tokens);
            controls.Children.Add(reset);
            AddRow(referenceRows, "composer.size_of_a_referenced_conversation", controls, Message("settings.most_a_quote_may_carry"));
            var feedback = Detail(string.Empty);
            feedback.IsVisible = false;
            referenceRows.Children.Add(feedback);
            tokens.Open(Message("settings.token_unit"), Message("composer.largest_quote"), "session-ref.tokens", CommitAsync);
            reset.Click += async (_, _) => await SaveAsync(0).ConfigureAwait(true);
            Apply();

            Task CommitAsync(string text)
            {
                if (busy)
                {
                    return Task.CompletedTask;
                }

                var minimum = (int)state["minTokens"]!;
                var maximum = (int)state["maxTokens"]!;
                if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < minimum || value > maximum)
                {
                    Notice(MessageCatalog.Get(
                        "common.enter_a_whole_number_from",
                        Language,
                        ("min", Number(minimum)),
                        ("max", Number(maximum))));
                    return Task.CompletedTask;
                }

                Notice(string.Empty);
                return value != (int)state["tokens"]! ? SaveAsync(value) : Task.CompletedTask;
            }

            async Task SaveAsync(int value)
            {
                if (busy)
                {
                    return;
                }

                busy = true;
                controls.IsEnabled = false;
                Notice(string.Empty);
                try
                {
                    state = await api.SetSessionReferenceAsync(value, _closing.Token).ConfigureAwait(true);
                    Apply();
                }
                catch (Exception error)
                {
                    if (!_closing.IsCancellationRequested)
                    {
                        Notice(DescribeError(error));
                    }
                }
                finally
                {
                    controls.IsEnabled = true;
                    busy = false;
                }
            }

            void Apply()
            {
                tokens.SetValue((int)state["tokens"]!);
                reset.IsEnabled = (int?)state["tokens"] != (int?)state["defaultTokens"];
            }

            void Notice(string value)
            {
                feedback.Text = value;
                feedback.IsVisible = value.Length > 0;
            }
        }

        async Task ReadClaimsAsync()
        {
            var state = (await ReadAgentPreferenceAsync(api.SubagentClaimsAsync).ConfigureAwait(true)).Setting;
            if (state is null)
            {
                claimRows.Children.Add(Detail(Message("subagent.could_not_read_the_sub")));
                return;
            }

            SegmentChoice(
                claimRows,
                "session.sub_agent_write_scope",
                "subagent-claims.optimistic",
                [new("task", Message("settings.per_task")), new("all", Message("settings.whole_workspace"))],
                (bool?)state["optimistic"] == true ? "task" : "all",
                id => api.SetSubagentClaimsAsync(id == "task", _closing.Token),
                Message("session.each_sub_agents_write_scope"),
                Message("subagent.a_sub_agent_that_names"),
                applyKey: "settings.rebuilds_the_runtime");
        }

        string Message(string key) => MessageCatalog.Get(key, Language);
    }

    /// <summary>Mounts the Automation cards in the Agent panel.</summary>
    /// <param name="column">The current settings page.</param>
    /// <param name="api">The active kernel.</param>
    /// <returns>The initial hook and schedule reads.</returns>
    private Task AgentAutomationAsync(StackPanel column, StudioPageClient api)
    {
        var card = Card(column, "settings.automation");
        var hooks = new HookSettingsView { Tag = "settings.hooks" };
        var schedules = new ScheduleSettingsView { Tag = "panels.scheduled_runs" };
        card.Children.Add(hooks);
        card.Children.Add(schedules);
        return Task.WhenAll(
            hooks.OpenAsync(api, Language, DescribeError, _closing.Token),
            schedules.OpenAsync(api, Language, DescribeError, _closing.Token));
    }

    /// <summary>Reads one independently available Agent preference.</summary>
    /// <param name="read">The kernel query.</param>
    /// <returns>The setting and a localized read error, if any.</returns>
    private async Task<(JsonObject? Setting, string? Error)> ReadAgentPreferenceAsync(Func<CancellationToken, Task<JsonObject?>> read)
    {
        try
        {
            return (await read(_closing.Token).ConfigureAwait(true), null);
        }
        catch (Exception error) when (!_closing.IsCancellationRequested)
        {
            return (null, DescribeError(error));
        }
    }
}
