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
    /// <param name="description">Supporting text.</param>
    /// <param name="tooltip">Additional context shown on hover.</param>
    /// <param name="isEnabled">Whether the current preference can be changed.</param>
    /// <param name="applyKey">The lifecycle label for this setting.</param>
    /// <param name="choiceNote">Supporting copy aligned under the choice.</param>
    private void SegmentChoice(StackPanel card, string key, string action, SectionEntry[] choices, string current, Func<string, Task> save, string? description = null, string? tooltip = null, bool isEnabled = true, string? applyKey = null, string? choiceNote = null)
    {
        var group = new WrapPanel { IsEnabled = isEnabled };
        var shell = new Border { Child = group, Padding = new Thickness(2), CornerRadius = new CornerRadius(7) };
        shell.Bind(Border.BackgroundProperty, shell.GetResourceObservable("OverlayBrush"));
        AutomationProperties.SetAutomationId(shell, "role:radiogroup");
        AutomationProperties.SetName(shell, MessageCatalog.Get(key, Language));
        var groupName = Guid.NewGuid().ToString("N");
        var options = new List<(string Id, RadioButton Button)>();
        foreach (var choice in choices)
        {
            var button = new RadioButton { GroupName = groupName, Content = choice.Title, IsChecked = choice.Id == current, Tag = choice.Id };
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

        Control field = shell;
        if (!string.IsNullOrEmpty(choiceNote))
        {
            shell.HorizontalAlignment = HorizontalAlignment.Right;
            var note = Detail(choiceNote);
            note.TextAlignment = Avalonia.Media.TextAlignment.Right;
            note.MaxWidth = 320;
            field = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Right, Children = { shell, note } };
        }

        var row = AddRow(card, key, field, description, applyKey);
        if (tooltip is not null)
        {
            ToolTip.SetTip(row, tooltip);
        }
    }

    /// <summary>Loads start-mode and update policy.</summary>
    /// <param name="card">The start card.</param>
    /// <param name="api">Host endpoints.</param>
    /// <returns>The preference reads.</returns>
    private async Task StartSettingsAsync(StackPanel card, StudioPageClient api)
    {
        var plan = await api.FlagAsync(StudioSetting.DefaultPlan, _closing.Token).ConfigureAwait(true);
        var modes = new[] { new SectionEntry("agent", "Agent"), new SectionEntry("plan", MessageCatalog.Get(MessageKey.Common.Plan, Language)) };
        SegmentChoice(
            card,
            "safety.how_new_conversations_start",
            "default-mode.plan",
            modes,
            plan ? "plan" : "agent",
            id => api.SetFlagAsync(StudioSetting.DefaultPlan, id == "plan", _closing.Token),
            applyKey: "settings.applies_to_new_sessions");
        var update = await api.UpdateModeAsync(_closing.Token).ConfigureAwait(true);
        if (update is not null)
        {
            var options = new[] { "manual", "notify", "download" }.Select(id => new SectionEntry(id, MessageCatalog.Get("about." + id, Language))).ToArray();
            Choice(card, "about.update_mode", options, update, id => api.SetUpdateModeAsync(id, _closing.Token));
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
        var toggles = new Dictionary<string, (ToggleSwitch Switch, Border Row)>(StringComparer.Ordinal);
        var descriptions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["enabled"] = "settings.a_system_notification_when_a",
            ["sound"] = "settings.off_by_default_plays_a",
            ["turnDone"] = "settings.the_answer_is_written_or",
            ["approval"] = "common.the_model_wants_to_touch",
            ["ask"] = "common.the_model_stopped_to_ask",
            ["feedbackReply"] = "settings.one_alert_when_a_report",
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
            var row = AddRow(fields, key, toggle, MessageCatalog.Get(descriptions[field], Language));
            row.Classes.Set("notification-option", field != "enabled");
            row.Classes.Set("notification-branch", field is not ("enabled" or "sound"));
            row.Classes.Set("notification-last", field == "diskLow");
            toggles[field] = (toggle, row);

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
            foreach (var (field, (toggle, row)) in toggles)
            {
                toggle.IsChecked = (bool?)prefs[field] == true;
                toggle.IsEnabled = active || field is "enabled" or "sound";
                row.Classes.Set("notification-inactive", !toggle.IsEnabled);
            }
        }
    }
}
