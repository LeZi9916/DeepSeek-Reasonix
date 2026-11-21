using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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
        ["model"] = ["common.model_services", "onboarding.connect_a_model_service", "models.automatically_check_connections_and_balances", "common.default_model"],
        ["session"] = ["persona.system_persona", "persona.agent_presets", "safety.question_wait_time"],
        ["session:persona"] = ["persona.start_of_the_system_prompt", "persona.additional_instructions", "persona.do_not_inject_optional_runtime"],
        ["tools"] = ["ext.tools_the_model_can_use", "common.built_in_browser"],
        ["ext"] = ["ext.installed"],
        ["ext:market"] = ["ext.community_market"],
        ["display"] = ["settings.fold_the_work_in_between", "settings.file_changes", "settings.ai_file_delivery_cards"],
        ["controls"] = ["settings.keyboard_shortcuts", "composer.send_shortcut", "settings.messages_sent_while_running"],
        ["appearance"] = ["settings.light_and_dark", "look.interface_theme", "look.typeface", "look.visual_effects", "look.interface_size"],
        ["storage"] = ["data.memory", "data.auto_archive_inactive_conversations"],
        ["network"] = ["models.proxy_mode", "models.test_saved_settings", "remote.add_a_machine"],
        ["account"] = ["settings.account", "about.current_version", "data.trace_export", "feedback.open_logs_folder"],
        ["advanced"] = ["settings.not_in_this_version_yet", "feedback.open_logs_folder"],
    };

    /// <summary>The section owning the current controls, including any nested page.</summary>
    private string _shownSettings = "common";

    /// <summary>Suppresses an intermediate top-level navigation during a deep jump.</summary>
    private bool _selectingSetting;

    /// <summary>Reports whether the currently mounted explicit-save form contains edits.</summary>
    private Func<bool>? _unsavedSettings;

    /// <summary>The leave decision presented to the user.</summary>
    private TaskCompletionSource<bool>? _leaveDecision;

    /// <summary>Counts native search destinations in a top-level section.</summary>
    /// <param name="section">Top-level identifier.</param>
    /// <returns>Number of indexed settings.</returns>
    private static int BlockCount(string section) => s_destinations.Where(p => p.Key.Split(':')[0] == section).SelectMany(p => p.Value).Distinct().Count();

    /// <summary>Connects searchable controls, keyboard navigation and the leave prompt.</summary>
    private void InitializeSettingsSearch()
    {
        SettingsPanel.SizeChanged += (_, e) => SettingsPanel.Classes.Set("compact", e.NewSize.Width <= 760);
        SettingsSearch.TextChanged += (_, _) => ShowSettingsHits();
        SettingsSearch.AddHandler(KeyDownEvent, OnSettingsSearchKey, RoutingStrategies.Tunnel);
        SettingsHits.Tapped += async (_, _) => await OpenSettingHitAsync().ConfigureAwait(true);
        SettingsHits.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await OpenSettingHitAsync().ConfigureAwait(true);
            }
        };
        KeepSettings.Click += (_, _) => _leaveDecision?.TrySetResult(false);
        DiscardSettings.Click += (_, _) => _leaveDecision?.TrySetResult(true);
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
                foreach (var label in labels)
                {
                    var title = MessageCatalog.Get(label, Language);
                    if (title.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                    {
                        hits.Add(new SettingHit(target, label, page, title));
                    }
                }
            }
        }

        SettingsHits.ItemsSource = hits;
        SettingsHits.SelectedIndex = hits.Count == 0 ? -1 : 0;
        SettingsHits.IsVisible = hits.Count != 0;
        NoSettingsHits.IsVisible = hits.Count == 0;
        NoSettingsHits.Text = MessageCatalog.Get(MessageKey.Chat.NoMatches, Language);
        SearchResultsPanel.IsVisible = true;
    }

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
            SearchResultsPanel.IsVisible = false;
        }
        else if (e.Key is Key.Up or Key.Down && SettingsHits.ItemCount > 0)
        {
            var n = SettingsHits.ItemCount;
            SettingsHits.SelectedIndex = (SettingsHits.SelectedIndex + (e.Key == Key.Up ? n - 1 : 1)) % n;
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
        var target = SettingsBody.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => Equals(c.Tag, hit.Key));
        if (target is not null)
        {
            target.BringIntoView();
            (target.Focusable ? target : target.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Focusable && c.IsEnabled))?.Focus();
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
        KeepSettings.Content = MessageCatalog.Get(MessageKey.Common.Cancel, Language);
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
