using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Lists the tool descriptions and shows a switch only for user-configurable tools.</summary>
public sealed partial class SafetyToolInventory : SettingsFormView
{
    /// <summary>Initializes a new instance of the <see cref="SafetyToolInventory"/> class.</summary>
    public SafetyToolInventory() => InitializeComponent();

    /// <summary>Loads the live tool inventory and enables editing user exclusions.</summary>
    /// <param name="api">The current session's tool endpoints.</param>
    /// <param name="language">The display language.</param>
    /// <param name="describe">Localizes a failure.</param>
    /// <param name="token">The host lifetime.</param>
    /// <returns>The first inventory read.</returns>
    internal async Task OpenAsync(StudioPageClient api, InterfaceLanguage language, Func<Exception, string> describe, CancellationToken token)
    {
        Translate(language);
        JsonObject? inventory = null;
        var busy = false;
        try
        {
            inventory = await api.ToolsAsync(token).ConfigureAwait(true);
            Draw();
        }
        catch (Exception)
        {
            if (!token.IsCancellationRequested)
            {
                Error.Text = Text("ext.could_not_read_the_tool");
                Error.IsVisible = true;
            }
        }

        void Draw()
        {
            ToolRows.Children.Clear();
            foreach (var item in (inventory?["tools"] as JsonArray)?.OfType<JsonObject>() ?? [])
            {
                if ((string?)item["name"] is not { } name)
                {
                    continue;
                }

                var required = (string?)item["requiredBy"];
                var project = (bool?)item["byProject"] == true;
                var description = !string.IsNullOrEmpty(required)
                    ? Text("ext.feature_needs_it_so_it", ("feature", Feature(required)))
                    : project ? Text("ext.this_projects_config_file_turned") : Blurb(name, (string?)item["description"]);
                var text = new StackPanel
                {
                    Spacing = 2,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap, LineHeight = 19 },
                        new TextBlock { Text = description, Classes = { "safety-tool-detail" } },
                    },
                };
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
                grid.Children.Add(text);
                if (string.IsNullOrEmpty(required) && !project)
                {
                    var toggle = new ToggleSwitch { Tag = name, IsChecked = (bool?)item["disabled"] != true };
                    AutomationProperties.SetAutomationId(toggle, "action:tool-list.toggle");
                    AutomationProperties.SetName(toggle, name);
                    Grid.SetColumn(toggle, 1);
                    grid.Children.Add(toggle);
                    toggle.IsCheckedChanged += async (_, _) =>
                    {
                        if (busy)
                        {
                            return;
                        }

                        busy = true;
                        ToolRows.IsEnabled = false;
                        Error.IsVisible = false;
                        var disable = toggle.IsChecked != true;
                        try
                        {
                            inventory = await api.SetToolDisabledAsync(name, disable, token).ConfigureAwait(true);
                            Draw();
                        }
                        catch (Exception error)
                        {
                            if (!token.IsCancellationRequested)
                            {
                                toggle.IsChecked = !disable;
                                Error.Text = describe(error);
                                Error.IsVisible = true;
                            }
                        }
                        finally
                        {
                            ToolRows.IsEnabled = true;
                            busy = false;
                        }
                    };
                }

                ToolRows.Children.Add(new Border { Child = grid, Classes = { "safety-tool-row" } });
            }
        }
    }

    /// <summary>Shows the feature name instead of an internal feature ID when known.</summary>
    /// <param name="id">The kernel feature ID.</param>
    /// <returns>The translated label.</returns>
    private string Feature(string id) => Text(id switch
    {
        "goal_mode" => "ext.goal_mode",
        "deferred_mcp_tools" => "ext.loading_deferred_mcp_tools",
        "plan_steps" => "ext.plan_step_tracking",
        "turn_exit" => "ext.ending_a_turn_the_host",
        _ => id,
    });

    /// <summary>Reuses the reference built-in tool descriptions and retains kernel text for other tools.</summary>
    /// <param name="name">Canonical tool name.</param>
    /// <param name="description">The kernel description.</param>
    /// <returns>The visible tool description.</returns>
    private string Blurb(string name, string? description)
    {
        var key = name switch
        {
            "advise" => "chat.ask_a_stronger_model_for",
            "ask" => "chat.ask_you_a_question_the",
            "await_user" => "chat.hand_control_back_to_you",
            "bash" => "chat.run_a_command_in_the",
            "bash_output" => "chat.read_the_new_output_of",
            "best_of_n" => "chat.run_one_well_specified_coding",
            "browser_act" => "chat.operate_a_browser_page_with",
            "browser_open" => "chat.open_a_web_page_and",
            "browser_read" => "chat.read_a_browser_page_a",
            "code_index" => "chat.built_in_lightweight_index_of",
            "complete_step" => "chat.record_that_one_step_of",
            "compress" => "chat.compress_a_chosen_part_of",
            "computer_act" => "chat.operate_an_application_you_have",
            "computer_read" => "chat.look_at_an_application_you",
            "conclude_blocked" => "chat.declare_once_the_reason_is",
            "context_budget" => "chat.report_how_much_context_room",
            "delete_range" => "chat.delete_a_contiguous_range_of",
            "delete_symbol" => "chat.delete_a_named_function_method",
            "dispatch_to_project" => "chat.hand_a_task_to_a",
            "open_projects" => "chat.ask_you_to_add_a",
            "docs" => "chat.search_and_read_the_official",
            "edit_file" => "chat.replace_one_exact_piece_of",
            "explore" => "chat.investigate_the_codebase_read_only",
            "fleet" => "chat.dispatch_2_64_sub_agent",
            "forget" => "chat.delete_a_saved_memory_by",
            "glob" => "chat.find_files_by_glob_pattern",
            "grep" => "chat.search_file_contents_with_a",
            "history" => "chat.search_your_saved_session_history",
            "import_web_image" => "chat.import_an_image_fetched_by",
            "install_skill" => "chat.write_and_save_a_new",
            "install_source" => "chat.plan_install_or_uninstall_a",
            "kill_shell" => "chat.stop_a_background_job_that",
            "list_peers" => "chat.list_the_other_reasonix_sessions",
            "list_sessions" => "chat.list_saved_conversations_newest_first",
            "list_subagents" => "chat.list_the_sub_agents_this",
            "ls" => "chat.list_a_directorys_entries_folders",
            "lsp_definition" => "chat.jump_to_where_a_symbol",
            "lsp_diagnostics" => "chat.report_a_files_compiler_and",
            "lsp_hover" => "chat.show_a_symbols_type_signature",
            "lsp_references" => "chat.list_every_reference_to_a",
            "memory" => "chat.search_list_and_read_this",
            "move_file" => "chat.move_or_rename_a_file",
            "multi_edit" => "chat.apply_several_edits_to_one",
            "notebook_edit" => "chat.edit_one_cell_of_a",
            "parallel_tasks" => "chat.run_several_read_only_sub",
            "present_files" => "chat.present_existing_final_files_to",
            "read_file" => "chat.read_a_text_file_optionally",
            "read_mcp_resource" => "chat.read_one_resource_a_document",
            "read_only_skill" => "chat.invoke_a_skill_read_only",
            "read_only_task" => "chat.start_a_read_only_research",
            "read_session" => "chat.read_a_saved_conversation_by",
            "read_skill" => "chat.load_an_inline_playbook_from",
            "read_subagent_result" => "chat.read_a_finished_sub_agents",
            "read_video" => "chat.look_at_a_video_natively",
            "recall" => "chat.bring_folded_conversation_back_read",
            "remember" => "chat.save_a_durable_background_fact",
            "research" => "chat.research_in_an_isolated_sub",
            "review" => "chat.review_the_pending_changes_in",
            "run_script" => "chat.run_a_short_starlark_python",
            "run_skill" => "chat.invoke_a_skill_from_the",
            "security_review" => "chat.security_review_the_current_branchs",
            "send_peer_message" => "chat.send_text_to_another_reasonix",
            "setting" => "chat.find_and_change_your_own",
            "slash_command" => "chat.invoke_a_project_slash_command",
            "task" => "chat.start_a_sub_agent_for",
            "todo_write" => "chat.record_and_update_the_task",
            "update_goal" => "chat.report_what_this_turn_did",
            "use_capability" => "chat.search_and_call_deferred_tools",
            "view_image" => "chat.look_at_an_image_file",
            "wait" => "chat.wait_for_background_jobs_to",
            "web_fetch" => "chat.fetch_a_url_and_return",
            "web_search" => "chat.search_the_web_for_current",
            "write_file" => "chat.write_full_file_content_overwriting",
            _ => null,
        };
        return key is null ? description ?? string.Empty : Text(key);
    }
}
