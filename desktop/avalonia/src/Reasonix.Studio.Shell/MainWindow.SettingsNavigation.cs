using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Search destinations backed by native controls.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> s_destinations = new Dictionary<string, string[]>
    {
        ["common"] = ["safety.how_new_conversations_start", "about.update_mode", "common.language", "composer.send_shortcut", "common.notifications", "settings.light_and_dark"],
        ["model"] = ["settings.models_by_use", "settings.context_maintenance", "common.model_services", "models.automatically_check_connections_and_balances", "common.default_model", "onboarding.connect_a_model_service"],
        ["session"] = ["safety.question_wait_time", "composer.size_of_a_referenced_conversation", "panels.long_stretches_without_progress", "settings.narration_between_tool_calls", "persona.agent_presets", "settings.plan_approval", "remote.messages_between_sessions_experimental", "session.sub_agent_write_scope", "panels.scheduled_runs", "settings.hooks", "persona.system_persona"],
        ["session:persona"] = ["persona.start_of_the_system_prompt", "persona.additional_instructions", "persona.do_not_inject_optional_runtime"],
        ["tools"] = ["settings.tool_approvals", "settings.outside_text", "ext.tools_the_model_can_use", "settings.rules_you_spell_out", "settings.sandbox", "common.which_program_runs_commands", "session.several_sessions_writing_one_project", "models.web_search_and_fetch", "common.built_in_browser"],
        ["ext"] = ["settings.plugin_packages", "ext.external_services", "settings.skills", "ext.installed"],
        ["ext:market"] = ["ext.community_market"],
        ["display"] = ["settings.file_changes", "settings.fold_the_work_in_between", "settings.ai_file_delivery_cards", "settings.new_answer_divider", "chat.transcript_folding", "plan.show_collapsed_plan_progress", "common.office_document_preview"],
        ["controls"] =
        [
            "settings.icon_rail", "settings.navigation_entries", "session.session_row_buttons",
            "settings.session_tabs_kept_open", "settings.session_tabs_kept_live", "settings.keep_finished_sessions_in_running",
            "settings.messages_sent_while_running", "settings.global_show_hide_shortcut", "settings.keyboard_shortcuts", "safety.esc_stops_a_task",
            "settings.idle_page_sleep", "session.session_tabs", "composer.composer_hint",
        ],
        ["appearance"] = ["settings.light_and_dark", "look.interface_theme", "look.typeface", "look.visual_effects", "look.interface_size"],
        ["storage"] = ["data.auto_archive_inactive_conversations", "session.where_this_session_writes", "data.export_and_import_config", "data.cloud_backup", "data.memory", "usage.cost_display_currency", "usage.upload_volume", "usage.usage_cost", "common.storage", "data.sessions_from_1_x"],
        ["network"] = ["common.network", "models.proxy_mode", "models.test_saved_settings", "settings.remote", "remote.add_a_machine"],
        ["account"] = ["data.trace_export", "settings.account", "common.version", "about.logs_and_abnormal_exit_records", "common.community_and_contributors", "about.support_the_project"],
    };

    /// <summary>The section owning the current controls, including any nested page.</summary>
    private string _shownSettings = "common";

    /// <summary>Suppresses an intermediate top-level navigation during a deep jump.</summary>
    private bool _selectingSetting;

    /// <summary>Reports whether the currently mounted explicit-save form contains edits.</summary>
    private Func<bool>? _unsavedSettings;

    /// <summary>Describes the fields changed in the current preset draft.</summary>
    private Func<IReadOnlyList<string>>? _unsavedSettingsSummary;

    /// <summary>The leave decision presented to the user.</summary>
    private TaskCompletionSource<bool>? _leaveDecision;

    /// <summary>Counts native search destinations in a top-level section.</summary>
    /// <param name="section">Top-level identifier.</param>
    /// <returns>Number of indexed settings.</returns>
    private static int BlockCount(string section) => section switch
    {
        "common" => 6,
        "model" => 4,
        "session" => 10,
        "tools" => 9,
        "ext" => 4,
        "display" => 7,
        "controls" => 13,
        "appearance" => 9,
        "storage" => 10,
        "network" => 3,
        "account" => 6,
        _ => s_destinations.Where(pair => pair.Key.Split(':')[0] == section).SelectMany(pair => pair.Value).Distinct().Count(),
    };

    /// <summary>Connects searchable controls, keyboard navigation and the leave prompt.</summary>
    private void InitializeSettingsSearch()
    {
        SettingsPanel.SizeChanged += (_, e) => SettingsPanel.Classes.Set("compact", e.NewSize.Width <= 760);
        SettingsNav.AddHandler(PointerWheelChangedEvent, ScrollCompactSettingsNav, RoutingStrategies.Tunnel);
        SettingsScroller.PointerWheelChanged += (_, _) => CancelSettingsScroll();
        SettingsScroller.ScrollChanged += (_, _) => PinSettingsIndex();
        SettingsHeadingBlock.SizeChanged += (_, _) => PinSettingsIndex();
        SettingsSearch.TextChanged += (_, _) => ShowSettingsHits();
        SettingsSearch.AddHandler(KeyDownEvent, OnSettingsSearchKey, RoutingStrategies.Tunnel);
        SettingsHits.Tapped += async (_, e) =>
        {
            if (e.Source is Avalonia.Visual visual && (visual as ListBoxItem ?? visual.FindAncestorOfType<ListBoxItem>())?.Content is SettingHit hit)
            {
                SettingsHits.SelectedItem = hit;
                await OpenSettingHitAsync().ConfigureAwait(true);
            }
        };
        SettingsHits.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await OpenSettingHitAsync().ConfigureAwait(true);
            }
        };
        KeepSettings.Click += (_, _) => _leaveDecision?.TrySetResult(false);
        DismissLeavePrompt.Click += (_, _) => _leaveDecision?.TrySetResult(false);
        SettingsLeavePrompt.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                _leaveDecision?.TrySetResult(false);
            }
        };
        DiscardSettings.Click += (_, _) => _leaveDecision?.TrySetResult(true);
    }

    /// <summary>Scrolls the compact tab strip horizontally with a vertical mouse wheel.</summary>
    /// <param name="sender">The settings navigation control.</param>
    /// <param name="e">The wheel delta.</param>
    private void ScrollCompactSettingsNav(object? sender, PointerWheelEventArgs e)
    {
        if (!SettingsPanel.Classes.Contains("compact") || e.Delta.X != 0 || e.Delta.Y == 0 ||
            SettingsNav.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is not { } scroll)
        {
            return;
        }

        scroll.Offset -= new Vector(e.Delta.Y * scroll.SmallChange.Width * 3, 0);
        e.Handled = true;
    }

    /// <summary>Offers sections and matching settings in the current language.</summary>
    private void ShowSettingsHits()
    {
        var query = SettingsSearch.Text?.Trim() ?? string.Empty;
        if (query.Length == 0 || !SettingsPanel.IsVisible)
        {
            SearchResultsPanel.IsVisible = false;
            return;
        }

        var hits = new List<SettingHit>();
        foreach (var (id, key) in s_sections)
        {
            var page = MessageCatalog.Get(key, Language);
            if (page.Contains(query, StringComparison.CurrentCultureIgnoreCase) || id.Equals(query, StringComparison.OrdinalIgnoreCase))
            {
                hits.Add(new SettingHit(id, string.Empty, page, page));
            }

            foreach (var (target, labels) in s_destinations.Where(p => p.Key.Split(':')[0] == id))
            {
                var path = target switch
                {
                    "session:persona" => page + " › " + MessageCatalog.Get("persona.system_persona", Language),
                    "ext:market" => page + " › " + MessageCatalog.Get("ext.community_market", Language),
                    _ => page,
                };
                foreach (var label in labels)
                {
                    var title = MessageCatalog.Get(label, Language);
                    if (SettingAvailable(label) && title.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                    {
                        hits.Add(new SettingHit(target, label, path, title));
                    }
                }
            }
        }

        SettingsHits.ItemsSource = hits;
        SettingsHits.SelectedIndex = hits.Count == 0 ? -1 : 0;
        SettingsHits.Classes.Remove("keyboard-search");
        SettingsHits.IsVisible = hits.Count != 0;
        NoSettingsHits.IsVisible = hits.Count == 0;
        NoSettingsHits.Text = MessageCatalog.Get("settings.no_matching_setting", Language);
        SearchResultsPanel.IsVisible = true;
    }

    /// <summary>Tests whether the local host exposes this setting.</summary>
    /// <param name="key">The setting's canonical key.</param>
    /// <returns>Whether search can open the setting.</returns>
    private bool SettingAvailable(string key) => key != "settings.global_show_hide_shortcut" || WindowShortcut is not null;

    /// <summary>Search owns its keys while its result list is open.</summary>
    /// <param name="sender">The search box.</param>
    /// <param name="e">Pressed key.</param>
    private async void OnSettingsSearchKey(object? sender, KeyEventArgs e)
    {
        if (!SearchResultsPanel.IsVisible)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            DismissSettingsSearch();
        }
        else if (e.Key is Key.Up or Key.Down && SettingsHits.ItemCount > 0)
        {
            var n = SettingsHits.ItemCount;
            SettingsHits.SelectedIndex = (SettingsHits.SelectedIndex + (e.Key == Key.Up ? n - 1 : 1)) % n;
            SettingsHits.Classes.Add("keyboard-search");
            SettingsHits.ScrollIntoView(SettingsHits.SelectedIndex);
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await OpenSettingHitAsync().ConfigureAwait(true);
        }
        else
        {
            return;
        }

        e.Handled = true;
    }

    /// <summary>Clears the query and returns keyboard focus to the search box.</summary>
    private void DismissSettingsSearch()
    {
        SettingsSearch.Text = string.Empty;
        SearchResultsPanel.IsVisible = false;
        SettingsSearch.Focus();
    }

    /// <summary>Opens the result's real section and brings the corresponding setting into view.</summary>
    /// <returns>The section read.</returns>
    private async Task OpenSettingHitAsync()
    {
        if (SettingsHits.SelectedItem is not SettingHit hit)
        {
            return;
        }

        SettingsSearch.Text = string.Empty;
        SearchResultsPanel.IsVisible = false;
        if (_shownSettings != hit.Section)
        {
            await ShowSectionAsync(hit.Section).ConfigureAwait(true);
        }

        if (_shownSettings != hit.Section || !SettingsPanel.IsVisible)
        {
            return;
        }

        SelectSettingsNav(hit.Section.Split(':')[0]);
        var anchor = hit.Key switch
        {
            "common.default_model" => "settings.models_by_use",
            "ext.installed" => "settings.plugin_packages",
            "onboarding.connect_a_model_service" => "common.model_services",
            _ => hit.Key,
        };
        var target = SettingsBody.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => Equals(c.Tag, anchor));
        if (target is not null)
        {
            FocusSetting(target);
        }
    }

    /// <summary>Selects the owning tab without starting a second request.</summary>
    /// <param name="id">Top-level section id.</param>
    private void SelectSettingsNav(string id)
    {
        _selectingSetting = true;
        try
        {
            SettingsNav.SelectedItem = SettingsNav.Items.OfType<SectionEntry>().FirstOrDefault(item => item.Id == id);
        }
        finally
        {
            _selectingSetting = false;
        }
    }

    /// <summary>Preserves a pending edit until the reader explicitly leaves.</summary>
    /// <returns>Whether navigation should proceed.</returns>
    private async Task<bool> MayLeaveSettingsAsync()
    {
        if (_unsavedSettings?.Invoke() != true)
        {
            return true;
        }

        if (_leaveDecision is not null)
        {
            return false;
        }

        _leaveDecision = new TaskCompletionSource<bool>();
        LeaveQuestion.Text = MessageCatalog.Get("persona.discard_unsaved_changes", Language);
        LeaveDetail.Text = MessageCatalog.Get("common.they_will_be_lost_if", Language);
        KeepSettings.Content = MessageCatalog.Get("persona.keep_editing", Language);
        AutomationProperties.SetName(DismissLeavePrompt, MessageCatalog.Get("common.close", Language));
        LeaveChanges.Children.Clear();
        foreach (var change in _unsavedSettingsSummary?.Invoke() ?? [])
        {
            LeaveChanges.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(8, 2),
                [!Border.BackgroundProperty] = StudioTypography.Resource("OverlayBrush"),
                Child = new TextBlock { Text = change, [!TextBlock.FontSizeProperty] = StudioTypography.Size(11) },
            });
        }

        LeaveChanges.IsVisible = LeaveChanges.Children.Count > 0;
        DiscardSettings.Content = MessageCatalog.Get("persona.discard_changes", Language);
        var previous = FocusManager?.GetFocusedElement();
        SettingsSheet.IsEnabled = false;
        SettingsTitleBar.IsEnabled = false;
        SearchResultsPanel.IsVisible = false;
        SettingsLeavePrompt.IsVisible = true;
        KeepSettings.Focus();
        try
        {
            return await _leaveDecision.Task.ConfigureAwait(true);
        }
        finally
        {
            _leaveDecision = null;
            SettingsLeavePrompt.IsVisible = false;
            SettingsSheet.IsEnabled = true;
            SettingsTitleBar.IsEnabled = true;
            previous?.Focus();
        }
    }

    /// <summary>A setting and its owning navigation destination.</summary>
    /// <param name="Section">Native page id.</param>
    /// <param name="Key">Row identity.</param>
    /// <param name="Page">Localized section name.</param>
    /// <param name="Title">Localized setting name.</param>
    private sealed record SettingHit(string Section, string Key, string Page, string Title);
}
