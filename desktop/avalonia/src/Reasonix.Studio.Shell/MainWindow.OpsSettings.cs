using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Host;
using Reasonix.Studio.Shell.Settings;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Builds the four cards for workspace interaction and performance.</summary>
    /// <param name="column">The mounted settings column.</param>
    private void ShowOpsSettings(StackPanel column)
    {
        var keys = Card(column, "settings.keyboard_shortcuts");
        var sheet = new KeyboardShortcutSheet { Tag = "settings.keyboard_shortcuts" };
        keys.Children.Add(sheet);
        sheet.Open(Language);
        SegmentChoice(
            keys,
            "safety.esc_stops_a_task",
            "appearance.esc-stop",
            [Item("confirm", "safety.press_esc_twice_to_stop"), Item("now", "safety.press_esc_once_to_stop")],
            _workspacePreferences.SingleEscapeStop ? "now" : "confirm",
            value => Commit(
                _workspacePreferences with
                {
                    SingleEscapeStop = value == "now",
                },
                DisarmStop),
            Message("safety.what_escape_does_while_a"));

        var navigation = Card(column, "settings.tabs_and_navigation_card");
        var tabs = new ToggleSwitch { IsChecked = _workspacePreferences.ShowSessionTabs };
        Avalonia.Automation.AutomationProperties.SetAutomationId(tabs, "action:appearance.session-tabs");
        AddRow(navigation, "session.session_tabs", tabs, Message("session.show_tabs_at_the_top"));
        tabs.IsCheckedChanged += (_, _) =>
        {
            SaveOps(_workspacePreferences with
            {
                ShowSessionTabs = tabs.IsChecked == true,
            });
            DrawPaneTabs();
        };
        SegmentChoice(
            navigation,
            "session.session_row_buttons",
            "appearance.session-quick-actions",
            [Item("off", "common.close"), Item("on", "common.show")],
            _workspacePreferences.SessionQuickActions ? "on" : "off",
            value => Commit(
                _workspacePreferences with
                {
                    SessionQuickActions = value == "on",
                },
                () => Projects.QuickActions = value == "on"),
            Message("settings.buttons_shown_on_row_hover"));
        SegmentChoice(
            navigation,
            "settings.icon_rail",
            "appearance.nav-rail",
            [Item("on", "settings.always"), Item("collapsed", "settings.only_when_the_sidebar_is"), Item("off", "settings.never")],
            _workspacePreferences.NavigationRail,
            value => Commit(
                _workspacePreferences with
                {
                    NavigationRail = value,
                },
                LayoutPanels),
            Message("settings.which_entries_the_left_rail_shows"));
        navigation.Children.Add(Detail(Message("settings.with_it_off_or_hidden")));
        var entries = new NavigationEntriesSettings { Tag = "settings.navigation_entries" };
        navigation.Children.Add(entries);
        entries.Open(Language, _workspacePreferences, (order, hidden) =>
        {
            SaveOps(_workspacePreferences with
            {
                NavigationOrder = order,
                HiddenNavigation = hidden,
            });
            DrawNavigationEntries();
        });
        SegmentChoice(
            navigation,
            "settings.keep_finished_sessions_in_running",
            "appearance.live-grace",
            [
                Item("0", "settings.off"), Item("10", "settings.n_10_minutes"), Item("30", "settings.n_30_minutes"),
                Item("60", "settings.n_1_hour"), Item("240", "settings.n_4_hours"),
            ],
            _workspacePreferences.LiveGraceMinutes.ToString(CultureInfo.InvariantCulture),
            value => Commit(
                _workspacePreferences with
                {
                    LiveGraceMinutes = int.Parse(value, CultureInfo.InvariantCulture),
                },
                ApplyLiveGrace),
            Message("settings.finished_sessions_stay_in_running"));

        var running = Card(column, "settings.while_running_card");
        SegmentChoice(
            running,
            "settings.messages_sent_while_running",
            "composer.running-message-intent",
            [Item("steer", "settings.steer_at_the_next_tool"), Item("followup", "settings.send_after_the_whole_run")],
            QueueDuringRun ? "followup" : "steer",
            value =>
            {
                QueueDuringRun = value == "followup";
                ShowComposerState(Conversation.State);
                return Task.CompletedTask;
            },
            Message("settings.what_a_message_sent_while_running_does"),
            choiceNote: Message("settings.use_send_now_in_the"));
        SegmentChoice(
            running,
            "composer.composer_hint",
            "appearance.composer-hint",
            [Item("off", "common.close"), Item("on", "common.show")],
            _workspacePreferences.ShowComposerPlaceholder ? "on" : "off",
            value => Commit(
                _workspacePreferences with
                {
                    ShowComposerPlaceholder = value == "on",
                },
                () => ShowComposerState(Conversation.State)),
            Message("settings.hint_text_in_the_composer"));

        var performance = Card(column, "settings.performance_card");
        var opens = new OpsLimitView { Tag = "settings.session_tabs_kept_open" };
        opens.Configure(
            Message("settings.session_tabs_kept_open"),
            Message("settings.default_0_nothing_is_folded"),
            Message("settings.limit_on_session_tabs"),
            Message("settings.n_0_means_no_limit"));
        performance.Children.Add(opens);
        var open = opens.Field;
        open.SetValue(_workspacePreferences.OpenSessionLimit);
        open.Open(Message("settings.tabs"), Message("settings.limit_on_session_tabs"), "appearance.open-session-limit", value =>
        {
            var limit = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0;
            SaveOps(_workspacePreferences with
            {
                OpenSessionLimit = limit,
            });
            open.SetValue(limit);
            if (_navigation is not null)
            {
                _navigation.Limit = limit;
            }

            return Task.CompletedTask;
        });
        var streams = new OpsLimitView { Tag = "settings.session_tabs_kept_live" };
        streams.Configure(
            Message("settings.session_tabs_kept_live"),
            Message("settings.empty_means_automatic_a_browser"),
            Message("settings.tabs_with_a_live_connection"),
            MessageCatalog.Get("settings.empty_is_automatic_1_to", Language, ("max", "4")));
        performance.Children.Add(streams);
        var budget = streams.Field;
        budget.SetOptionalValue(_workspacePreferences.LiveStreamLimit, Message("common.auto"));
        budget.Open(Message("settings.tabs"), Message("settings.tabs_with_a_live_connection"), "appearance.live-stream-budget", value =>
        {
            var limit = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= 4 ? number : (int?)null;
            SaveOps(_workspacePreferences with
            {
                LiveStreamLimit = limit,
            });
            budget.SetOptionalValue(limit, Message("common.auto"));
            ReconcilePaneStreams();
            return Task.CompletedTask;
        });
        SegmentChoice(
            performance,
            "settings.idle_page_sleep",
            "appearance.page-sleep",
            [Item("off", "settings.keep_open"), Item("on", "settings.on_after_15_minutes")],
            _workspacePreferences.SleepIdlePages ? "on" : "off",
            value => Commit(
                _workspacePreferences with
                {
                    SleepIdlePages = value == "on",
                },
                ApplyBrowserSleep),
            Message("settings.how_long_before_a_page_sleeps"));

        string Message(string key) => MessageCatalog.Get(key, Language);

        SectionEntry Item(string id, string key) => new(id, Message(key));

        Task Commit(WorkspacePreferences next, Action apply)
        {
            SaveOps(next);
            apply();
            return Task.CompletedTask;
        }
    }

    /// <summary>Applies retention to the running filter without extending the actual turn lifetime.</summary>
    private void ApplyLiveGrace() => Projects.KeepFinishedFor(_workspacePreferences.LiveGraceMinutes);

    /// <summary>Applies a device preference to this window and persists it for production launches.</summary>
    /// <param name="preferences">The new device preferences.</param>
    private void SaveOps(WorkspacePreferences preferences)
    {
        _workspacePreferences = preferences;
        if (_connectOnOpen)
        {
            _workspacePreferences.Save();
        }
    }
}
