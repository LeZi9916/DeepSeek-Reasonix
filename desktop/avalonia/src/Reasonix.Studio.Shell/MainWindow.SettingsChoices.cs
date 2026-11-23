using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Displays a short mutually exclusive preference as inline choices.</summary>
    /// <param name="card">The owning card.</param>
    /// <param name="key">Label key.</param>
    /// <param name="action">Shared action id.</param>
    /// <param name="choices">Selectable values.</param>
    /// <param name="current">Current value.</param>
    /// <param name="save">Writes a new value.</param>
    private void SegmentChoice(StackPanel card, string key, string action, SectionEntry[] choices, string current, Func<string, Task> save)
    {
        var group = new WrapPanel();
        var shell = new Border { Child = group, Padding = new Thickness(2), CornerRadius = new CornerRadius(7) };
        shell.Bind(Border.BackgroundProperty, shell.GetResourceObservable("OverlayBrush"));
        AutomationProperties.SetAutomationId(shell, "role:radiogroup");
        AutomationProperties.SetName(shell, MessageCatalog.Get(key, Language));
        var groupName = Guid.NewGuid().ToString("N");
        var options = new List<(string Id, RadioButton Button)>();
        foreach (var choice in choices)
        {
            var button = new RadioButton { GroupName = groupName, Content = choice.Title, IsChecked = choice.Id == current };
            button.Classes.Add("segmented");
            AutomationProperties.SetAutomationId(button, "action:" + action);
            options.Add((choice.Id, button));
            group.Children.Add(button);
            button.IsCheckedChanged += async (_, _) =>
            {
                if (!group.IsEnabled || button.IsChecked != true || current == choice.Id)
                {
                    return;
                }

                group.IsEnabled = false;
                try
                {
                    await save(choice.Id).ConfigureAwait(true);
                    current = choice.Id;
                }
                catch (Exception error)
                {
                    if (ReferenceEquals(TopLevel.GetTopLevel(card), this))
                    {
                        SettingsError.Text = DescribeError(error);
                    }

                    foreach (var option in options)
                    {
                        option.Button.IsChecked = option.Id == current;
                    }
                }
                finally
                {
                    group.IsEnabled = true;
                }
            };
        }

        AddRow(card, key, shell);
    }

    /// <summary>Loads start-mode and update policy.</summary>
    /// <param name="card">The start card.</param>
    /// <param name="api">Host endpoints.</param>
    /// <returns>The preference reads.</returns>
    private async Task StartSettingsAsync(StackPanel card, StudioPageClient api)
    {
        var plan = await api.FlagAsync(StudioSetting.DefaultPlan, _closing.Token).ConfigureAwait(true);
        var modes = new[] { new SectionEntry("agent", "Agent"), new SectionEntry("plan", MessageCatalog.Get(MessageKey.Common.Plan, Language)) };
        Choice(card, "safety.how_new_conversations_start", modes, plan ? "plan" : "agent", id => api.SetFlagAsync(StudioSetting.DefaultPlan, id == "plan", _closing.Token));
        var update = await api.UpdateModeAsync(_closing.Token).ConfigureAwait(true);
        if (update is not null)
        {
            var options = new[] { "manual", "notify", "download" }.Select(id => new SectionEntry(id, MessageCatalog.Get("about." + id, Language))).ToArray();
            Choice(card, "about.update_mode", options, update, id => api.SetUpdateModeAsync(id, _closing.Token));
        }
    }

    /// <summary>Shows working shortcuts supported by the native window.</summary>
    /// <param name="card">The keyboard card.</param>
    private void ShortcutReference(StackPanel card)
    {
        var primary = OperatingSystem.IsMacOS() ? "⌘" : "Ctrl+";
        foreach (var (label, chord) in new[]
        {
            (MessageKey.Common.StartANewSession, "Alt+N"),
            (MessageKey.App.ShowTheWorkspaceRail, primary + "B"),
            (MessageKey.Chat.SearchSessionsOrProjects, primary + "K"),
            (MessageKey.Chat.Search, primary + "F"),
            (MessageKey.Common.Settings, primary + ","),
        })
        {
            AddRow(card, label, new TextBlock { Text = chord, [!TextBlock.FontSizeProperty] = StudioTypography.Size(12) });
        }
    }

    /// <summary>Reads and changes notifications through the desktop that owns them.</summary>
    /// <param name="card">The input and reminder card.</param>
    /// <param name="api">The desktop endpoints.</param>
    /// <returns>The initial read.</returns>
    private async Task NotificationsAsync(StackPanel card, StudioPageClient api)
    {
        var prefs = await api.NotificationsAsync(_closing.Token).ConfigureAwait(true);
        if (prefs is null)
        {
            return;
        }

        var fields = new StackPanel();
        card.Children.Add(fields);
        var toggles = new Dictionary<string, ToggleSwitch>(StringComparer.Ordinal);
        var descriptions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["enabled"] = "settings.a_system_notification_when_a",
            ["sound"] = "settings.off_by_default_plays_a",
            ["turnDone"] = "settings.the_answer_is_written_or",
            ["approval"] = "common.the_model_wants_to_touch",
            ["ask"] = "common.the_model_stopped_to_ask",
            ["diskLow"] = "settings.warns_when_the_drive_holding",
        };
        foreach (var (field, key, name) in new[]
        {
            ("enabled", "common.notifications", "enabled"),
            ("sound", "settings.play_a_sound", "sound"),
            ("turnDone", "settings.when_the_turn_ends", "turn-done"),
            ("approval", "common.when_something_waits_for_approval", "approval"),
            ("ask", "common.when_the_model_asks_you", "ask"),
            ("feedbackReply", "settings.when_feedback_gets_a_reply", "feedback-reply"),
            ("diskLow", "settings.when_disk_space_is_low", "disk-low"),
        })
        {
            var toggle = new ToggleSwitch { OffContent = string.Empty, OnContent = string.Empty, MinWidth = 0 };
            AutomationProperties.SetAutomationId(toggle, "action:notify." + name);
            toggles[field] = toggle;
            AddRow(fields, key, toggle, descriptions.TryGetValue(field, out var description) ? MessageCatalog.Get(description, Language) : null);
            if (field is not ("enabled" or "sound") && fields.Children[^1] is Border row)
            {
                row.Margin = new Avalonia.Thickness(18, 0, 0, 0);
            }

            toggle.Click += async (_, _) =>
            {
                fields.IsEnabled = false;
                try
                {
                    var accepted = await api.SetNotificationAsync(field, toggle.IsChecked == true, _closing.Token).ConfigureAwait(true);
                    if (accepted is null)
                    {
                        fields.IsVisible = false;
                    }
                    else
                    {
                        prefs = accepted;
                    }
                }
                catch (Exception error)
                {
                    if (ReferenceEquals(TopLevel.GetTopLevel(fields), this))
                    {
                        SettingsError.Text = DescribeError(error);
                    }
                }
                finally
                {
                    ShowValues();
                    fields.IsEnabled = true;
                }
            };
        }

        ShowValues();

        void ShowValues()
        {
            var active = (bool?)prefs["enabled"] == true || (bool?)prefs["sound"] == true;
            foreach (var (field, toggle) in toggles)
            {
                toggle.IsChecked = (bool?)prefs[field] == true;
                toggle.IsEnabled = active || field is "enabled" or "sound";
            }
        }
    }

    /// <summary>Lists tool availability, including kernel-declared requirements and project overrides.</summary>
    /// <param name="card">The owning settings card.</param>
    /// <param name="api">Kernel endpoints.</param>
    /// <returns>The tool inventory.</returns>
    private async Task ToolSettingsAsync(StackPanel card, StudioPageClient api)
    {
        var inventory = await api.ToolsAsync(_closing.Token).ConfigureAwait(true);
        var list = new StackPanel();
        var disclosure = new Expander { Header = MessageCatalog.Get("ext.tools_the_model_can_use", Language), Content = list, HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "ext.tools_the_model_can_use" };
        card.Children.Add(disclosure);
        Draw(inventory);

        void Draw(JsonObject snapshot)
        {
            list.Children.Clear();
            foreach (var tool in snapshot["tools"]?.AsArray().OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            {
                if ((string?)tool["name"] is not { } name)
                {
                    continue;
                }

                var toggle = new CheckBox { Content = name, IsChecked = (bool?)tool["disabled"] != true };
                var required = (string?)tool["requiredBy"];
                toggle.IsEnabled = string.IsNullOrEmpty(required) && (bool?)tool["byProject"] != true;
                var hint = !string.IsNullOrEmpty(required)
                    ? MessageCatalog.Get("ext.feature_needs_it_so_it", Language, ("feature", required))
                    : (bool?)tool["byProject"] == true ? MessageCatalog.Get("ext.this_projects_config_file_turned", Language) : (string?)tool["description"];
                ToolTip.SetTip(toggle, hint);
                list.Children.Add(toggle);
                toggle.Click += async (_, _) =>
                {
                    list.IsEnabled = false;
                    try
                    {
                        inventory = await api.SetToolDisabledAsync(name, toggle.IsChecked != true, _closing.Token).ConfigureAwait(true);
                    }
                    catch (Exception error)
                    {
                        if (ReferenceEquals(TopLevel.GetTopLevel(list), this))
                        {
                            SettingsError.Text = DescribeError(error);
                        }
                    }
                    finally
                    {
                        Draw(inventory);
                        list.IsEnabled = true;
                    }
                };
            }
        }
    }

    /// <summary>Reads the installed and available application versions.</summary>
    /// <param name="card">Version and diagnostics card.</param>
    /// <param name="api">Desktop endpoints.</param>
    /// <returns>The version read.</returns>
    private async Task VersionSettingsAsync(StackPanel card, StudioPageClient api)
    {
        var reading = await api.VersionsAsync(false, _closing.Token).ConfigureAwait(true);
        if (reading is null)
        {
            return;
        }

        var current = new TextBlock { Text = (string?)reading["current"] };
        var latest = Detail((string?)reading["latest"] ?? string.Empty);
        AddRow(card, "about.current_version", current);
        AddRow(card, "common.version", latest);
        AddAction(card, "common.reload", async () =>
        {
            var next = await api.VersionsAsync(true, _closing.Token).ConfigureAwait(true);
            if (next is not null)
            {
                current.Text = (string?)next["current"];
                latest.Text = (string?)next["err"] is { Length: > 0 } error ? error : (string?)next["latest"];
            }
        });
    }
}
