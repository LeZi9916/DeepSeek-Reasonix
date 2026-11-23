using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

/// <summary>The scheduled-run board, editor and review screen in Agent settings.</summary>
public sealed partial class ScheduleSettingsView : SettingsFormView
{
    /// <summary>The supported intervals shown in the reference selector.</summary>
    private static readonly (int Seconds, string Label)[] s_intervals =
    [
        (3600, "panels.every_hour"), (7200, "panels.every_2_hours"), (21600, "panels.every_6_hours"),
        (43200, "panels.every_12_hours"), (86400, "panels.every_day"), (604800, "panels.every_week"),
    ];

    /// <summary>Initializes a new instance of the <see cref="ScheduleSettingsView"/> class.</summary>
    public ScheduleSettingsView() => InitializeComponent();

    /// <summary>Mounts the board and attaches the visible actions to the kernel.</summary>
    /// <param name="api">The schedule endpoints of the active host.</param>
    /// <param name="language">The page's language.</param>
    /// <param name="describe">Localizes a refusal.</param>
    /// <param name="token">The host lifetime.</param>
    /// <returns>The first board read.</returns>
    internal async Task OpenAsync(StudioPageClient api, InterfaceLanguage language, Func<Exception, string> describe, CancellationToken token)
    {
        Translate(language);
        JsonObject? board = null;
        JsonObject? reviewedDraft = null;
        string? confirm = null;
        string? deleting = null;
        var locked = false;
        var busy = false;
        var query = 0;
        var poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        poll.Tick += async (_, _) =>
        {
            if (IsEffectivelyVisible && !busy)
            {
                await LoadAsync().ConfigureAwait(true);
            }
        };
        AttachedToVisualTree += (_, _) => poll.Start();
        DetachedFromVisualTree += (_, _) => poll.Stop();

        Retry.Click += async (_, _) => await LoadAsync().ConfigureAwait(true);
        PauseAll.Click += async (_, _) => await ActAsync(async () =>
        {
            await api.SetSchedulesPausedAsync((bool?)board?["pausedAll"] != true, token).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
        NewSchedule.Click += (_, _) =>
        {
            Folder.SelectedIndex = 0;
            Frequency.SelectedItem = Frequency.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, 86400) && item.IsEnabled)
                ?? Frequency.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.IsEnabled);
            ScheduleLabel.Text = Prompt.Text = string.Empty;
            Error.IsVisible = Review.IsVisible = false;
            Form.IsVisible = true;
            UpdateActions();
        };
        Cancel.Click += (_, _) =>
        {
            Form.IsVisible = Review.IsVisible = false;
            UpdateActions();
        };
        Back.Click += (_, _) =>
        {
            Review.IsVisible = false;
            Form.IsVisible = true;
            UpdateActions();
        };
        Folder.SelectionChanged += (_, _) => UpdateActions();
        Frequency.SelectionChanged += (_, _) => UpdateActions();
        Prompt.TextChanged += (_, _) => UpdateActions();
        TrustFolder.Click += async (_, _) => await ActAsync(async () =>
        {
            await api.TrustCurrentWorkspaceAsync(token).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
        Preview.Click += async (_, _) =>
        {
            if (Folder.SelectedItem is not Option folder || folder.Id.Length == 0 ||
                Frequency.SelectedItem is not ComboBoxItem { Tag: int interval } || string.IsNullOrWhiteSpace(Prompt.Text))
            {
                return;
            }

            var draft = new JsonObject
            {
                ["workspace"] = folder.Id,
                ["prompt"] = Prompt.Text.Trim(),
                ["label"] = ScheduleLabel.Text ?? string.Empty,
                ["everySec"] = interval,
            };
            await ActAsync(async () =>
            {
                var preview = await api.PreviewScheduleAsync(draft, token).ConfigureAwait(true);
                reviewedDraft = draft;
                confirm = (string?)preview["confirm"];
                RenderReview(preview);
            }).ConfigureAwait(true);
        };
        Create.Click += async (_, _) =>
        {
            if (reviewedDraft is null || string.IsNullOrEmpty(confirm))
            {
                return;
            }

            var request = reviewedDraft.DeepClone().AsObject();
            request["confirm"] = confirm;
            await ActAsync(async () =>
            {
                await api.CreateScheduleAsync(request, token).ConfigureAwait(true);
                Review.IsVisible = Form.IsVisible = false;
                reviewedDraft = null;
                confirm = null;
                await LoadAsync().ConfigureAwait(true);
            }).ConfigureAwait(true);
        };
        CloseResult.Click += (_, _) => ResultPanel.IsVisible = false;
        await LoadAsync().ConfigureAwait(true);
        if (this.IsAttachedToVisualTree() && !token.IsCancellationRequested)
        {
            poll.Start();
        }

        async Task LoadAsync()
        {
            var request = ++query;
            try
            {
                var next = await api.SchedulesAsync(token).ConfigureAwait(true);
                if (request != query)
                {
                    return;
                }

                if (next is null)
                {
                    LoadError.Text = Text("panels.could_not_read_the_scheduled", ("reason", Text("common.failed")));
                    LoadIssue.IsVisible = true;
                    return;
                }

                LoadIssue.IsVisible = false;
                if (!JsonNode.DeepEquals(board, next))
                {
                    board = next;
                    RenderBoard();
                }
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested && request == query)
                {
                    LoadError.Text = Text(board is null ? "panels.could_not_read_the_scheduled" : "panels.could_not_refresh_the_scheduled", ("reason", describe(error)));
                    LoadIssue.IsVisible = true;
                }
            }
        }

        async Task ActAsync(Func<Task> action)
        {
            if (busy)
            {
                return;
            }

            busy = true;
            Board.IsEnabled = false;
            Error.IsVisible = false;
            try
            {
                await action().ConfigureAwait(true);
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    if (!locked && error is KernelRefusal { Code: "schedule.editing_disabled" })
                    {
                        locked = true;
                        RenderBoard();
                    }

                    Error.Text = describe(error);
                    Error.IsVisible = true;
                    ReadOnly.IsVisible = locked;
                }
            }
            finally
            {
                busy = false;
                Board.IsEnabled = true;
                UpdateActions();
            }
        }

        void RenderBoard()
        {
            if (board is null)
            {
                return;
            }

            Board.IsVisible = true;
            var schedules = Items(board["schedules"]).ToArray();
            var pausedAll = (bool?)board["pausedAll"] == true;
            PauseLabel.Text = Text(pausedAll ? "panels.all_scheduled_runs_are_paused" : "panels.scheduled_runs_are_on");
            PauseAll.Content = Text(pausedAll ? "panels.resume_all" : "panels.pause_all");
            AutomationProperties.SetAutomationId(PauseAll, pausedAll ? "action:schedule.resume-all" : "action:schedule.pause-all");
            Policy.Text = Text(
                "panels.at_most_n_at_least",
                ("n", Count((int?)board["policy"]?["maxSchedules"])),
                ("m", Count((int?)board["policy"]?["minIntervalMinutes"])),
                ("k", Count((int?)board["policy"]?["globalTokensDay"])));
            Empty.IsVisible = schedules.Length == 0;
            Schedules.Children.Clear();
            foreach (var schedule in schedules)
            {
                AddSchedule(schedule);
            }

            Runs.Children.Clear();
            var runs = Items(board["runs"]).Take(8).ToArray();
            Recent.IsVisible = runs.Length > 0;
            foreach (var run in runs)
            {
                AddRun(run, schedules);
            }

            var selected = (Folder.SelectedItem as Option)?.Id;
            var folders = Items(board["folders"]).Select(item => new Option((string?)item["path"] ?? string.Empty, item["name"] + " — " + item["path"])).ToArray();
            Folder.ItemsSource = folders.Prepend(new Option(string.Empty, Text("panels.choose_a_trusted_project"))).ToArray();
            Folder.SelectedItem = Folder.Items.OfType<Option>().FirstOrDefault(option => option.Id == selected) ?? Folder.Items[0];
            Folder.IsVisible = folders.Length > 0;
            Trust.IsVisible = (bool?)board["current"]?["trustable"] == true;
            TrustMessage.Text = Text(
                (string?)board["current"]?["trust"] == "declined" ? "panels.the_open_project_name_was" : "panels.the_open_project_name_is",
                ("name", (string?)board["current"]?["name"] ?? string.Empty));
            NoFolder.IsVisible = !Trust.IsVisible && !Folder.IsVisible;
            var every = (Frequency.SelectedItem as ComboBoxItem)?.Tag;
            var minimumSeconds = 60 * ((int?)board["policy"]?["minIntervalMinutes"] ?? 0);
            var choices = s_intervals.Select(item => new ComboBoxItem
            {
                Content = Text(item.Label),
                Tag = item.Seconds,
                IsEnabled = item.Seconds >= minimumSeconds,
            }).ToArray();
            Frequency.ItemsSource = choices;
            Frequency.SelectedItem = choices.FirstOrDefault(item => Equals(item.Tag, every))
                ?? choices.FirstOrDefault(option => Equals(option.Tag, 86400) && option.IsEnabled)
                ?? choices.FirstOrDefault(option => option.IsEnabled);
            UpdateActions();
        }

        void AddSchedule(JsonObject schedule)
        {
            if ((string?)schedule["id"] is not { } id)
            {
                return;
            }

            var status = (string?)schedule["status"];
            var pausedReason = (string?)schedule["pausedReason"];
            var time = (long?)schedule["atUnix"] is { } once
                ? Text("panels.once_at", ("at", When(once)))
                : Interval((int?)schedule["everySec"] ?? 0);
            var state = time + " · " + Text(StatusLabel(status));
            if (!string.IsNullOrEmpty(pausedReason))
            {
                state += " · " + Text(PauseReason(pausedReason));
            }

            var detail = Text(
                "panels.used_a_of_b_tokens",
                ("a", Count((int?)schedule["spentLifetimeTokens"])),
                ("b", Count((int?)schedule["lifetimeTokens"])),
                ("d", When((long?)schedule["expiresAtUnix"])));
            var switches = new WrapPanel { ItemSpacing = 4, LineSpacing = 4, VerticalAlignment = VerticalAlignment.Center };
            var verb = status == "active" ? "pause" : "resume";
            switches.Children.Add(ScheduleAction(id, verb, Text("panels." + verb), locked && verb == "resume"));
            switches.Children.Add(ScheduleAction(id, "renew", Text("panels.renew"), locked));
            if (deleting == id)
            {
                switches.Children.Add(ScheduleAction(id, "delete", Text("common.confirm_delete"), false));
            }
            else
            {
                var delete = SmallAction(Text("common.delete"), "schedule.delete-ask", id);
                delete.Classes.Remove("preset-act");
                delete.Classes.Add("preset-flat");
                delete.Classes.Add("preset-muted");
                delete.Classes.Add("schedule-ghost");
                delete.Click += (_, _) =>
                {
                    deleting = id;
                    RenderBoard();
                };
                switches.Children.Add(delete);
            }

            var summary = new[] { state, $"{schedule["workspace"]} · {schedule["model"]}", detail };
            Schedules.Children.Add(Row(ScheduleName(schedule), summary, switches));
        }

        Button ScheduleAction(string id, string verb, string label, bool disabled)
        {
            var button = SmallAction(label, "schedule." + verb, id);
            button.IsEnabled = !disabled;
            button.Click += async (_, _) => await ActAsync(async () =>
            {
                await api.ScheduleActionAsync(id, verb, token).ConfigureAwait(true);
                deleting = null;
                await LoadAsync().ConfigureAwait(true);
            }).ConfigureAwait(true);
            return button;
        }

        void AddRun(JsonObject run, JsonObject[] schedules)
        {
            var id = (string?)run["triggerId"] ?? string.Empty;
            var scheduleId = (string?)run["scheduleId"] ?? string.Empty;
            var schedule = schedules.FirstOrDefault(item => (string?)item["id"] == scheduleId);
            var state = (string?)run["state"];
            var title = (schedule is null ? scheduleId : ScheduleName(schedule)) + " · " + Text(RunState(state));
            var details = When((long?)run["slotUnix"]) + " · " + Count((int?)run["chargedTokens"]) + " " + Text("panels.tokens");
            if ((string?)run["skipReason"] is { Length: > 0 } skip)
            {
                details += " · " + Text(SkipReason(skip));
            }

            Button? result = null;
            if (state is not ("skipped" or "claimed"))
            {
                result = SmallAction(Text("panels.view_result"), "schedule.result", id);
                result.Click += async (_, _) => await ActAsync(async () =>
                {
                    var report = await api.ScheduleResultAsync(id, token).ConfigureAwait(true);
                    RenderResult(report);
                }).ConfigureAwait(true);
            }

            Runs.Children.Add(Row(title, [details], result));
        }

        void RenderResult(JsonObject result)
        {
            ResultPanel.IsVisible = true;
            ResultHeading.Text = Text("panels.result") + " · " + When((long?)result["recordedUnix"]);
            Report.Text = (string?)result["report"] ?? string.Empty;
            Truncated.IsVisible = (bool?)result["reportTruncated"] == true;
            Pending.Children.Clear();
            foreach (var item in Items(result["pending"]))
            {
                Pending.Children.Add(Row((string?)item["summary"] ?? string.Empty, [(string?)item["detail"] ?? string.Empty], null));
            }

            PendingPanel.IsVisible = Pending.Children.Count > 0;
        }

        void RenderReview(JsonObject preview)
        {
            ReviewRows.Children.Clear();
            Add("panels.first_run", When((long?)preview["firstRunUnix"]));
            Add("panels.folder", $"{preview["workspace"]} {Text("panels.read_only_it_can_read")}");
            Add("common.models", (string?)preview["model"] ?? string.Empty);
            Add("panels.what_to_do", (string?)preview["prompt"] ?? string.Empty);
            Add(
                "panels.each_run_at_most",
                Text(
                    "panels.a_tokens_b_steps_c",
                    ("a", Count((int?)preview["perRunTokens"])),
                    ("b", Count((int?)preview["perRunSteps"])),
                    ("c", Count((int?)preview["perRunWallSec"]))));
            Add(
                "panels.in_total_at_most",
                Text(
                    "panels.a_tokens_a_day_b",
                    ("a", Count((int?)preview["perDayMax"])),
                    ("b", Count((int?)preview["lifetimeTokens"])),
                    ("d", When((long?)preview["expiresAtUnix"]))));
            Form.IsVisible = false;
            Review.IsVisible = true;

            void Add(string label, string value) => ReviewRows.Children.Add(Row(Text(label), [value], null));
        }

        string Interval(int seconds)
        {
            var label = s_intervals.FirstOrDefault(interval => interval.Seconds == seconds).Label;
            return label is not null ? Text(label) : Text("panels.every_n_minutes", ("n", Count((int)Math.Round(seconds / 60d))));
        }

        void UpdateActions()
        {
            PauseAll.IsEnabled = !locked || (bool?)board?["pausedAll"] != true;
            NewSchedule.IsVisible = !Form.IsVisible && !Review.IsVisible;
            NewSchedule.IsEnabled = !locked && (board?["schedules"] as JsonArray)?.Count < (int?)board?["policy"]?["maxSchedules"];
            Preview.IsEnabled = !locked && Folder.SelectedItem is Option { Id.Length: > 0 } &&
                Frequency.SelectedItem is ComboBoxItem { IsEnabled: true } && !string.IsNullOrWhiteSpace(Prompt.Text);
            Create.IsEnabled = !locked && !string.IsNullOrEmpty(confirm);
        }
    }

    /// <summary>Reads the objects in a schedule response list.</summary>
    /// <param name="node">The list on the wire.</param>
    /// <returns>Each record.</returns>
    private static IEnumerable<JsonObject> Items(JsonNode? node) => (node as JsonArray)?.OfType<JsonObject>() ?? [];

    /// <summary>Formats schedule limits and usage counts.</summary>
    /// <param name="value">The integer count.</param>
    /// <returns>The number with locale grouping.</returns>
    private static string Count(int? value) => (value ?? 0).ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Chooses a label for an unnamed schedule.</summary>
    /// <param name="schedule">The schedule record.</param>
    /// <returns>The name or workspace leaf.</returns>
    private static string ScheduleName(JsonObject schedule)
    {
        var label = (string?)schedule["label"];
        var root = (string?)schedule["workspace"] ?? string.Empty;
        return !string.IsNullOrEmpty(label) ? label : root.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? root;
    }

    /// <summary>Maps a schedule status to its displayed label.</summary>
    /// <param name="status">The kernel status.</param>
    /// <returns>The label key.</returns>
    private static string StatusLabel(string? status) => status switch
    {
        "active" => "common.running",
        "paused" => "app.held",
        "expired" => "panels.expired",
        "circuit-open" => "panels.stopped_by_the_safety_breaker",
        _ => status ?? string.Empty,
    };

    /// <summary>Maps an automatic pause reason.</summary>
    /// <param name="reason">The kernel code.</param>
    /// <returns>The label key.</returns>
    private static string PauseReason(string reason) => reason switch
    {
        "user" => "panels.you_paused_it",
        "consecutive_failures" => "panels.paused_automatically_after_repeated_failures",
        "breaker" => "panels.paused_automatically_to_protect_the",
        "lifetime_budget" => "panels.the_lifetime_budget_is_used",
        "corrupt" => "panels.its_record_is_damaged",
        _ => reason,
    };

    /// <summary>Maps the result of a recorded run.</summary>
    /// <param name="status">The run state.</param>
    /// <returns>The label key.</returns>
    private static string RunState(string? status) => status switch
    {
        "succeeded" => "common.done_2",
        "failed" => "common.failed",
        "blocked" => "panels.waiting_for_you",
        "budget_stopped" => "panels.budget_used_up",
        "interrupted" => "panels.interrupted",
        "skipped" => "panels.skipped_2",
        "claimed" => "panels.about_to_start",
        "running" => "common.running",
        _ => status ?? string.Empty,
    };

    /// <summary>Maps the reason a particular run was skipped.</summary>
    /// <param name="reason">The kernel reason.</param>
    /// <returns>The label key.</returns>
    private static string SkipReason(string reason) => reason switch
    {
        "missed" => "panels.missed_its_time",
        "budget_window" => "panels.the_budget_window_is_full",
        "lease_held" or "concurrency" => "panels.another_run_was_in_progress",
        _ => reason,
    };

    /// <summary>Creates a short schedule action.</summary>
    /// <param name="label">The localized label.</param>
    /// <param name="action">The reference action ID.</param>
    /// <param name="id">The schedule or result ID.</param>
    /// <returns>The button.</returns>
    private static Button SmallAction(string label, string action, string id)
    {
        var button = new Button { Content = label, Tag = id, Classes = { "preset-act" }, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(button, "action:" + action);
        return button;
    }

    /// <summary>Lays out one schedule, run, pending decision or review field.</summary>
    /// <param name="title">The row label.</param>
    /// <param name="details">The displayed detail lines.</param>
    /// <param name="actions">Optional controls on the right.</param>
    /// <returns>The row.</returns>
    private static Border Row(string title, IEnumerable<string> details, Control? actions)
    {
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.Medium, TextWrapping = TextWrapping.Wrap, LineHeight = 22.75 });
        foreach (var line in details.Where(value => value.Length > 0))
        {
            text.Children.Add(new TextBlock
            {
                Text = line,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22.75,
                [!TextBlock.ForegroundProperty] = StudioTypography.Resource("MutedBrush"),
            });
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = actions is null ? 0 : 24 };
        grid.Children.Add(text);
        if (actions is not null)
        {
            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);
        }

        return new Border { Child = grid, Classes = { "schedule-line" } };
    }

    /// <summary>Formats the time supplied by the schedule service.</summary>
    /// <param name="unix">Unix seconds.</param>
    /// <returns>The local date and time.</returns>
    private string When(long? unix)
    {
        if (unix is null or < 0 or > 253402300799)
        {
            return string.Empty;
        }

        var culture = Language == InterfaceLanguage.Zh ? CultureInfo.GetCultureInfo("zh-CN") : CultureInfo.CurrentCulture;
        return DateTimeOffset.FromUnixTimeSeconds(unix.Value).ToLocalTime().ToString(Language == InterfaceLanguage.Zh ? "yyyy年M月d日 HH:mm" : "MMM d, yyyy, h:mm tt", culture);
    }
}
