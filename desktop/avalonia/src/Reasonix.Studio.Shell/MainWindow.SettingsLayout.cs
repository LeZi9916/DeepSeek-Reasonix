using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Host;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The same page descriptions as the reference settings navigation.</summary>
    private static readonly IReadOnlyDictionary<string, string> s_descriptions = new Dictionary<string, string>
    {
        ["common"] = "settings.the_few_settings_changed_most",
        ["model"] = "settings.which_service_to_use_which",
        ["session"] = "settings.how_it_thinks_how_it",
        ["tools"] = "settings.what_it_can_touch_approval",
        ["ext"] = "settings.skills_external_tools_plugins_automation",
        ["display"] = "settings.what_the_conversation_shows_and",
        ["controls"] = "settings.shortcuts_tabs_sending_while_running",
        ["appearance"] = "settings.light_or_dark_colours_fonts",
        ["storage"] = "settings.memory_usage_storage_backup",
        ["network"] = "settings.network_remote_machines_phone",
        ["account"] = "settings.account_version_logs_community",
    };

    /// <summary>Gets the translated purpose of the current page.</summary>
    /// <param name="id">Top-level section.</param>
    /// <returns>Localized description.</returns>
    private string SectionDescription(string id) => s_descriptions.TryGetValue(id, out var key) ? MessageCatalog.Get(key, Language) : string.Empty;

    /// <summary>Creates a card containing the settings of one subject.</summary>
    /// <param name="column">Owning page column.</param>
    /// <param name="key">Heading key.</param>
    /// <returns>The rows inside this card.</returns>
    private StackPanel Card(StackPanel column, string key)
    {
        var rows = new StackPanel();
        var card = new Border { Child = rows, Tag = key };
        card.Classes.Add("settings-card");
        AutomationProperties.SetAutomationId(card, "class:prefs-card");
        rows.Children.Add(new TextBlock
        {
            Text = MessageCatalog.Get(key, Language),
            FontWeight = FontWeight.SemiBold,
            [!TextBlock.FontSizeProperty] = StudioTypography.Size(16),
            Margin = new Thickness(0, 0, 0, 14),
        });
        column.Children.Add(card);
        return rows;
    }

    /// <summary>Draws secondary text using the active theme.</summary>
    /// <param name="text">Visible detail.</param>
    /// <returns>The wrapping label.</returns>
    private TextBlock Detail(string text)
    {
        var label = new TextBlock { Text = text, [!TextBlock.FontSizeProperty] = StudioTypography.Size(12), TextWrapping = TextWrapping.Wrap };
        label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("FaintBrush"));
        return label;
    }

    /// <summary>Adds a labelled setting, stacking long-form editors below their labels.</summary>
    /// <param name="content">The settings card.</param>
    /// <param name="key">Translated label.</param>
    /// <param name="control">Live control.</param>
    /// <param name="description">Optional supporting text.</param>
    private void AddRow(StackPanel content, string key, Control control, string? description = null)
    {
        var label = MessageCatalog.Get(key, Language);
        var row = new Border { Tag = key };
        row.Classes.Add("setting-row");
        AutomationProperties.SetAutomationId(row, "class:srow");
        AutomationProperties.SetName(control, label);
        var name = new TextBlock { Text = label, [!TextBlock.FontSizeProperty] = StudioTypography.Size(13), FontWeight = FontWeight.Medium, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(name, "class:srow-name");
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        copy.Children.Add(name);
        description ??= key switch
        {
            "common.language" => MessageCatalog.Get("settings.interface_language", Language),
            "composer.send_shortcut" => MessageCatalog.Get("composer.send_with_enter_or_with", Language),
            "settings.light_and_dark" => MessageCatalog.Get("settings.follow_the_system_light_or", Language),
            "safety.how_new_conversations_start" => MessageCatalog.Get("safety.which_mode_a_new_chat", Language),
            "settings.fold_the_work_in_between" => MessageCatalog.Get("settings.tool_calls_and_thinking_fold", Language),
            "settings.messages_sent_while_running" => MessageCatalog.Get("settings.what_a_message_sent_while_running_does", Language),
            "safety.question_wait_time" => MessageCatalog.Get("settings.skip_after_no_answer", Language),
            _ => null,
        };
        if (!string.IsNullOrWhiteSpace(description))
        {
            var hint = Detail(description);
            AutomationProperties.SetAutomationId(hint, "class:srow-desc");
            copy.Children.Add(hint);
        }

        if (control is TextBox { AcceptsReturn: true })
        {
            var stack = new StackPanel { Spacing = 8 };
            stack.Children.Add(copy);
            stack.Children.Add(control);
            row.Child = stack;
        }
        else
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };
            grid.Children.Add(copy);
            Grid.SetColumn(control, 1);
            control.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(control);
            row.Child = grid;
        }

        content.Children.Add(row);
    }

    /// <summary>Indexes the settings actually mounted on this page.</summary>
    /// <param name="column">The current settings column.</param>
    private void IndexSettings(StackPanel column)
    {
        var targets = column.GetLogicalDescendants().OfType<Control>()
            .Where(c => c.Tag is string key && s_destinations.Values.Any(keys => keys.Contains(key, StringComparer.Ordinal)))
            .DistinctBy(c => c.Tag).ToArray();
        SettingsLinks.Children.Clear();
        SettingsIndex.IsVisible = targets.Length >= 3;
        if (!SettingsIndex.IsVisible)
        {
            return;
        }

        SettingsLinks.Children.Add(new TextBlock
        {
            Text = MessageCatalog.Get("settings.on_this_page", Language),
            [!TextBlock.FontSizeProperty] = StudioTypography.Size(12),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        foreach (var target in targets)
        {
            var link = new Button { Content = MessageCatalog.Get((string)target.Tag!, Language) };
            link.Classes.Add("page-link");
            AutomationProperties.SetAutomationId(link, "action:settings.page-index");
            link.Click += (_, _) =>
            {
                target.BringIntoView();
                var field = target as Button ?? target.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Focusable && c.IsEnabled);
                field?.Focus();
            };
            SettingsLinks.Children.Add(link);
        }
    }

    /// <summary>Opens the kernel log folder from settings.</summary>
    /// <param name="content">The current card.</param>
    private void LogsAction(StackPanel content) => AddAction(content, MessageKey.Feedback.OpenLogsFolder, async () =>
    {
        var refusal = await new AvaloniaHost(this).RevealLogsAsync().ConfigureAwait(true);
        if (refusal is not null)
        {
            SettingsError.Text = refusal.Error;
        }
    });
}
