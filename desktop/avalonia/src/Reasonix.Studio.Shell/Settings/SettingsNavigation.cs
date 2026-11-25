using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Owns settings navigation, search destinations and unsaved form decisions.</summary>
internal sealed class SettingsNavigation
{
    /// <summary>Sections in reference-frontend navigation order.</summary>
    private static readonly (string Id, string Title)[] s_sections =
    {
        ("common", "settings.common"), ("model", "common.models"), ("session", "settings.agent_behaviour"),
        ("tools", "settings.permissions_and_safety"), ("ext", "ext.extension"), ("display", "settings.conversation_display"),
        ("controls", "settings.controls_and_performance"), ("appearance", "settings.appearance"), ("storage", "settings.data"),
        ("network", "usage.connections"), ("account", "settings.account_and_about"),
    };

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

    /// <summary>A decision that a mounted form is waiting on.</summary>
    private TaskCompletionSource<bool>? _leave;

    /// <summary>Gets the top-level settings sections in navigation order.</summary>
    internal static IReadOnlyList<(string Id, string Title)> Sections => s_sections;

    /// <summary>Gets the native settings destinations.</summary>
    internal static IReadOnlyDictionary<string, string[]> Destinations => s_destinations;

    /// <summary>Gets or sets the current settings section.</summary>
    internal string Section
    {
        get; set;
    } = "common";

    /// <summary>Gets or sets a value indicating whether a deep jump owns tab selection.</summary>
    internal bool Selecting
    {
        get; set;
    }

    /// <summary>Gets or sets the dirty check for the mounted form.</summary>
    internal Func<bool>? Dirty
    {
        get; set;
    }

    /// <summary>Gets or sets the description of the mounted form's edits.</summary>
    internal Func<IReadOnlyList<string>>? Changes
    {
        get; set;
    }

    /// <summary>Gets a value indicating whether the mounted form has unsaved edits.</summary>
    internal bool HasEdits => Dirty?.Invoke() == true;

    /// <summary>Gets a value indicating whether a confirmation is already open.</summary>
    internal bool IsConfirming => _leave is not null;

    /// <summary>Counts native search destinations in a top-level section.</summary>
    /// <param name="section">Top-level identifier.</param>
    /// <returns>Number of indexed settings.</returns>
    internal static int BlockCount(string section) => section switch
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
        _ => Destinations.Where(pair => pair.Key.Split(':')[0] == section).SelectMany(pair => pair.Value).Distinct().Count(),
    };

    /// <summary>Returns matching settings in native section order.</summary>
    /// <param name="query">Localized query.</param>
    /// <param name="language">Current language.</param><param name="available">Whether the host exposes a setting.</param>
    /// <returns>Matching sections and anchors.</returns>
    internal static IReadOnlyList<SettingHit> Search(
        string query,
        InterfaceLanguage language,
        Func<string, bool> available)
    {
        var hits = new List<SettingHit>();
        foreach (var (id, key) in s_sections)
        {
            var page = MessageCatalog.Get(key, language);
            if (page.Contains(query, StringComparison.CurrentCultureIgnoreCase) || id.Equals(query, StringComparison.OrdinalIgnoreCase))
            {
                hits.Add(new SettingHit(id, string.Empty, page, page));
            }

            foreach (var (target, labels) in Destinations.Where(p => p.Key.Split(':')[0] == id))
            {
                var path = target switch
                {
                    "session:persona" => page + " › " + MessageCatalog.Get("persona.system_persona", language),
                    "ext:market" => page + " › " + MessageCatalog.Get("ext.community_market", language),
                    _ => page,
                };
                foreach (var label in labels)
                {
                    var title = MessageCatalog.Get(label, language);
                    if (available(label) && title.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                    {
                        hits.Add(new SettingHit(target, label, path, title));
                    }
                }
            }
        }

        return hits;
    }

    /// <summary>Starts an interactive leave decision.</summary>
    /// <returns>The decision to leave the section.</returns>
    internal Task<bool> BeginConfirmation()
    {
        _leave = new TaskCompletionSource<bool>();
        return _leave.Task;
    }

    /// <summary>Completes the pending leave decision.</summary>
    /// <param name="leave">Whether the user approved leaving.</param>
    /// <returns>Whether a decision was pending.</returns>
    internal bool ResolveConfirmation(bool leave)
    {
        var pending = _leave;
        _leave = null;
        return pending?.TrySetResult(leave) == true;
    }

    /// <summary>A setting and its owning navigation destination.</summary>
    /// <param name="Section">Native page id.</param>
    /// <param name="Key">Row identity.</param>
    /// <param name="Page">Localized section name.</param>
    /// <param name="Title">Localized setting name.</param>
    internal sealed record SettingHit(string Section, string Key, string Page, string Title);
}
