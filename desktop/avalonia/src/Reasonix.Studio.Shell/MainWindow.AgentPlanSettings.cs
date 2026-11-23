using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Settings;
using System;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Formats a preference limit without grouping separators.</summary>
    /// <param name="count">The value returned by the kernel.</param>
    /// <returns>The display value.</returns>
    private static string Number(int count) => count.ToString(CultureInfo.InvariantCulture);

    /// <summary>Shows the plan, question, narration and progress settings.</summary>
    /// <param name="column">The Agent settings column.</param>
    /// <param name="api">The active kernel.</param>
    /// <returns>The preference reads.</returns>
    private async Task AgentPlanSettingsAsync(StackPanel column, StudioPageClient api)
    {
        var card = Card(column, "settings.plans_and_questions");
        var approval = ReadAgentPreferenceAsync(api.PlanApprovalAsync);
        var timeout = ReadAgentPreferenceAsync(api.AskTimeoutAsync);
        var narration = ReadAgentPreferenceAsync(api.TurnNarrationAsync);
        var progress = ReadAgentPreferenceAsync(api.ProgressWatchAsync);
        await Task.WhenAll(approval, timeout, narration, progress).ConfigureAwait(true);

        if ((await approval.ConfigureAwait(true)).Setting is { } approved)
        {
            SegmentChoice(
                card,
                "settings.plan_approval",
                "plan-approval.auto",
                [new("ask", Message("settings.every_time")), new("auto", Message("settings.not_needed"))],
                (bool?)approved["auto"] == true ? "auto" : "ask",
                id => api.SetPlanApprovalAsync(id == "auto", _closing.Token),
                Message("settings.show_plans_before_running"),
                Message("plan.a_submitted_plan_starts_without"));
        }
        else
        {
            card.Children.Add(Detail(Message("plan.could_not_read_the_plan")));
        }

        if ((await timeout.ConfigureAwait(true)).Setting is { } questionTimeout)
        {
            AddQuestionTimeout(card, api, questionTimeout);
        }
        else
        {
            card.Children.Add(Detail(Message("safety.the_question_wait_setting_could")));
        }

        var (voice, voiceError) = await narration.ConfigureAwait(true);
        SegmentChoice(
            card,
            "settings.narration_between_tool_calls",
            "chrome.turn-narration",
            [new("brief", Message("settings.brief")), new("detailed", Message("settings.detailed"))],
            (string?)voice?["level"] ?? "brief",
            id => api.SetTurnNarrationAsync(id, _closing.Token),
            Message("settings.say_what_a_step_did"),
            isEnabled: voice is not null);
        if (voiceError is not null)
        {
            card.Children.Add(Detail(voiceError));
        }

        if ((await progress.ConfigureAwait(true)).Setting is { } thresholds)
        {
            AddProgressWatch(card, api, thresholds);
        }
        else
        {
            card.Children.Add(Detail(Message("panels.could_not_read_the_progress")));
        }

        string Message(string key) => MessageCatalog.Get(key, Language);
    }

    /// <summary>Edits the question timeout with the kernel-provided range and switch default.</summary>
    /// <param name="card">The settings card.</param>
    /// <param name="api">The active kernel.</param>
    /// <param name="initial">The current timeout configuration.</param>
    private void AddQuestionTimeout(StackPanel card, StudioPageClient api, JsonObject initial)
    {
        var state = initial;
        var busy = false;
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var seconds = new SettingsUnitField { Width = 96 };
        var enabled = new ToggleSwitch();
        AutomationProperties.SetAutomationId(enabled, "action:ask-timeout.enabled");
        controls.Children.Add(seconds);
        controls.Children.Add(enabled);
        AddRow(card, "safety.question_wait_time", controls, MessageCatalog.Get("settings.skip_after_no_answer", Language));
        AutomationProperties.SetName(enabled, MessageCatalog.Get("safety.skip_a_question_nobody_answers", Language));
        var explanation = Detail(string.Empty);
        explanation.IsVisible = false;
        card.Children.Add(explanation);
        Read();
        seconds.Open(
            MessageCatalog.Get("common.s", Language),
            MessageCatalog.Get("safety.wait_time", Language),
            "ask-timeout.seconds",
            async text =>
            {
                if (busy || enabled.IsChecked != true)
                {
                    return;
                }

                var min = (int?)state["minSeconds"] ?? 10;
                var max = (int?)state["maxSeconds"] ?? 86400;
                if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
                {
                    Show(MessageCatalog.Get("common.enter_a_whole_number_from", Language, ("min", Number(min)), ("max", Number(max))));
                }
                else if (value != (int?)state["seconds"])
                {
                    await SaveAsync(value).ConfigureAwait(true);
                }
                else
                {
                    Show(string.Empty);
                }
            });
        enabled.IsCheckedChanged += async (_, _) =>
        {
            if (!busy)
            {
                await SaveAsync(enabled.IsChecked == true ? (int?)state["defaultSeconds"] ?? 300 : 0).ConfigureAwait(true);
            }
        };

        void Read()
        {
            var current = (int?)state["seconds"] ?? 0;
            enabled.IsChecked = current > 0;
            seconds.IsVisible = current > 0;
            seconds.SetValue(current > 0 ? current : (int?)state["defaultSeconds"] ?? 300);
            Show((bool?)state["invalid"] == true
                ? MessageCatalog.Get(
                    "safety.the_question_wait_in_the",
                    Language,
                    ("min", Number((int?)state["minSeconds"] ?? 10)),
                    ("max", Number((int?)state["maxSeconds"] ?? 86400)),
                    ("path", (string?)state["path"] ?? string.Empty))
                : string.Empty);
        }

        void Show(string text)
        {
            explanation.Text = text;
            explanation.IsVisible = text.Length > 0;
        }

        async Task SaveAsync(int value)
        {
            busy = true;
            controls.IsEnabled = false;
            try
            {
                state = await api.SetAskTimeoutAsync(value, _closing.Token).ConfigureAwait(true);
                Read();
            }
            catch (Exception error)
            {
                if (!_closing.IsCancellationRequested)
                {
                    Read();
                    Show(DescribeError(error));
                }
            }
            finally
            {
                controls.IsEnabled = true;
                busy = false;
            }
        }
    }

    /// <summary>Edits the run-progress switch and the two thresholds it uses.</summary>
    /// <param name="card">The settings card.</param>
    /// <param name="api">The active kernel.</param>
    /// <param name="initial">The current thresholds.</param>
    private void AddProgressWatch(StackPanel card, StudioPageClient api, JsonObject initial)
    {
        var state = initial;
        var busy = false;
        var pause = new ToggleSwitch();
        AutomationProperties.SetAutomationId(pause, "action:progress-watch.pause");
        AddRow(card, "panels.long_stretches_without_progress", pause, MessageCatalog.Get("settings.pause_when_stuck", Language));
        AutomationProperties.SetName(pause, MessageCatalog.Get("panels.pause_a_task_that_stops", Language));
        var thresholds = new StackPanel();
        var rounds = new SettingsUnitField { Width = 96 };
        var multiple = new SettingsUnitField { Width = 120 };
        var roundsRow = AddRow(
            thresholds,
            "panels.rounds_without_progress",
            rounds,
            MessageCatalog.Get("panels.tool_rounds_in_a_row", Language, ("n", Number((int?)initial["defaultRounds"] ?? 20))));
        var multipleRow = AddRow(
            thresholds,
            "panels.input_token_backstop",
            multiple,
            MessageCatalog.Get("panels.a_model_independent_backstop_the", Language, ("n", Number((int?)initial["defaultTokenMultiple"] ?? 8))));
        roundsRow.Classes.Add("progress-threshold");
        multipleRow.Classes.Add("progress-threshold");
        multipleRow.Classes.Add("progress-threshold-last");
        card.Children.Add(new Border { Child = thresholds, Classes = { "progress-thresholds" } });

        var explanation = Detail(string.Empty);
        explanation.IsVisible = false;
        card.Children.Add(explanation);
        Read();
        rounds.Open(
            MessageCatalog.Get("panels.rounds", Language),
            MessageCatalog.Get("panels.rounds_without_progress", Language),
            "progress-watch.rounds",
            text => CommitAsync(text, "rounds"));
        multiple.Open(
            MessageCatalog.Get("panels.window", Language),
            MessageCatalog.Get("panels.input_token_backstop", Language),
            "progress-watch.token-multiple",
            text => CommitAsync(text, "tokenMultiple"));
        pause.IsCheckedChanged += async (_, _) =>
        {
            if (!busy)
            {
                await SaveAsync(pause.IsChecked == true, (int)state["rounds"]!, (int)state["tokenMultiple"]!).ConfigureAwait(true);
            }
        };

        void Read()
        {
            pause.IsChecked = (bool?)state["pause"] == true;
            rounds.SetValue((int?)state["rounds"] ?? 20);
            multiple.SetValue((int?)state["tokenMultiple"] ?? 8);
        }

        void Show(string text)
        {
            explanation.Text = text;
            explanation.IsVisible = text.Length > 0;
        }

        Task CommitAsync(string text, string field)
        {
            if (busy)
            {
                return Task.CompletedTask;
            }

            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value is < 1 or > 1000)
            {
                Show(MessageCatalog.Get("common.enter_a_whole_number_from_2", Language, ("n", "1000")));
                return Task.CompletedTask;
            }

            Show(string.Empty);
            if (value == (int?)state[field])
            {
                return Task.CompletedTask;
            }

            return SaveAsync(
                pause.IsChecked == true,
                field == "rounds" ? value : (int)state["rounds"]!,
                field == "tokenMultiple" ? value : (int)state["tokenMultiple"]!);
        }

        async Task SaveAsync(bool pausing, int count, int windowMultiple)
        {
            busy = true;
            pause.IsEnabled = thresholds.IsEnabled = false;
            Show(string.Empty);
            try
            {
                state = await api.SetProgressWatchAsync(pausing, count, windowMultiple, _closing.Token).ConfigureAwait(true);
                Read();
            }
            catch (Exception error)
            {
                if (!_closing.IsCancellationRequested)
                {
                    Read();
                    Show(DescribeError(error));
                }
            }
            finally
            {
                pause.IsEnabled = thresholds.IsEnabled = true;
                busy = false;
            }
        }
    }
}
