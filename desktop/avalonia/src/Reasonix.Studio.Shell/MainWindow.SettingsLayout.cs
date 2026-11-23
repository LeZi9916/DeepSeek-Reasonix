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

    /// <summary>Gets the translated purpose and setting count of the current page.</summary>
    /// <param name="id">Top-level section.</param>
    /// <returns>Localized description.</returns>
    private string SectionDescription(string id)
    {
        var count = BlockCount(id);
        var items = count == 0 ? string.Empty : MessageCatalog.Get("settings.n_items", Language, ("n", Format.Count(count, Language)));
        return s_descriptions.TryGetValue(id, out var key)
            ? MessageCatalog.Get(key, Language) + "\u2002·\u2002" + items
            : items;
    }

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
            [!TextBlock.FontSizeProperty] = StudioTypography.Size(17),
            LineHeight = 23.8,
            Margin = new Thickness(0, 0, 0, 12),
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
    /// <param name="applyKey">Optional translated notice describing when the setting takes effect.</param>
    /// <returns>The mounted row.</returns>
    private Border AddRow(StackPanel content, string key, Control control, string? description = null, string? applyKey = null)
    {
        var label = MessageCatalog.Get(key, Language);
        var row = new Border { Tag = key };
        row.Classes.Add("setting-row");
        AutomationProperties.SetAutomationId(row, "class:srow");
        AutomationProperties.SetName(control, label);
        var name = new TextBlock { Text = label, [!TextBlock.FontSizeProperty] = StudioTypography.Size(13), TextWrapping = TextWrapping.Wrap };
        name.Classes.Add("setting-row-name");
        AutomationProperties.SetAutomationId(name, "class:srow-name");
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        if (applyKey is null)
        {
            copy.Children.Add(name);
        }
        else
        {
            var apply = new Border
            {
                Child = new TextBlock { Text = MessageCatalog.Get(applyKey, Language), VerticalAlignment = VerticalAlignment.Center },
            };
            apply.Classes.Add("setting-apply");
            name.VerticalAlignment = VerticalAlignment.Center;
            var heading = new WrapPanel { Children = { name, apply } };
            copy.Children.Add(heading);
        }

        description ??= key switch
        {
            "common.language" => MessageCatalog.Get("settings.interface_language", Language),
            "composer.send_shortcut" => MessageCatalog.Get("composer.send_with_enter_or_with", Language),
            "settings.light_and_dark" => MessageCatalog.Get("settings.follow_the_system_light_or", Language),
            "safety.how_new_conversations_start" => MessageCatalog.Get("safety.which_mode_a_new_chat", Language),
            "about.update_mode" => MessageCatalog.Get("about.whether_to_check_for_and", Language),
            "settings.fold_the_work_in_between" => MessageCatalog.Get("settings.tool_calls_and_thinking_fold", Language),
            "settings.file_changes" => MessageCatalog.Get("settings.show_n_files_changed_above_input", Language),
            "settings.ai_file_delivery_cards" => MessageCatalog.Get("settings.generated_files_as_clickable_cards", Language),
            "settings.messages_sent_while_running" => MessageCatalog.Get("settings.what_a_message_sent_while_running_does", Language),
            "safety.question_wait_time" => MessageCatalog.Get("settings.skip_after_no_answer", Language),
            _ => null,
        };
        if (!string.IsNullOrWhiteSpace(description))
        {
            var hint = Detail(description);
            hint.Classes.Add("setting-row-detail");
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
        return row;
    }

    /// <summary>Indexes the settings actually mounted on this page.</summary>
    /// <param name="column">The current settings column.</param>
    private void IndexSettings(StackPanel column)
    {
        var targets = column.GetLogicalDescendants().OfType<Control>()
            .Where(c => c.Tag is string key && s_destinations.Values.Any(keys => keys.Contains(key, StringComparer.Ordinal)))
            .Where(c => _shownSettings != "network" || c.Tag is "common.network" or "settings.remote" or "remote.phone_access")
            .DistinctBy(c => c.Tag).ToArray();
        if (_shownSettings is "ext" or "ext:market")
        {
            IndexExtensionSettings();
            return;
        }

        if (_shownSettings == "storage")
        {
            IndexDataSettings(targets);
            return;
        }

        if (_shownSettings.StartsWith("session", StringComparison.Ordinal))
        {
            targets = targets.Where(target => s_destinations["session"].Contains((string)target.Tag!, StringComparer.Ordinal) && target.Tag is not "persona.system_persona")
                .OrderBy(target => Array.IndexOf(s_destinations["session"], (string)target.Tag!))
                .ToArray();
        }

        if (_shownSettings is "display" or "model" or "tools" or "controls" or "account")
        {
            targets = targets.OrderBy(target => Array.IndexOf(s_destinations[_shownSettings], (string)target.Tag!)).ToArray();
        }

        SettingsLinks.Children.Clear();
        SettingsIndex.IsVisible = targets.Length >= 2;
        if (!SettingsIndex.IsVisible)
        {
            return;
        }

        var label = Detail(MessageCatalog.Get("settings.on_this_page", Language));
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(0, 0, 8, 6);
        SettingsLinks.Children.Add(label);
        foreach (var target in targets)
        {
            var link = new Button { Content = MessageCatalog.Get((string)target.Tag!, Language) };
            link.Classes.Add("page-link");
            AutomationProperties.SetAutomationId(link, "action:settings.page-index");
            link.Click += (_, _) => FocusSetting(target);
            SettingsLinks.Children.Add(link);
        }
    }

    /// <summary>Keeps the reference extension index visible across both tabs.</summary>
    private void IndexExtensionSettings()
    {
        SettingsLinks.Children.Clear();
        SettingsIndex.IsVisible = true;
        var label = Detail(MessageCatalog.Get("settings.on_this_page", Language));
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(0, 0, 8, 6);
        SettingsLinks.Children.Add(label);
        foreach (var (key, section) in new[]
        {
            ("ext.community_market", "ext:market"),
            ("settings.plugin_packages", "ext"),
            ("ext.external_services", "ext"),
            ("settings.skills", "ext"),
        })
        {
            var link = new Button { Content = MessageCatalog.Get(key, Language), Classes = { "page-link" } };
            AutomationProperties.SetAutomationId(link, "action:settings.page-index");
            link.Click += async (_, _) =>
            {
                if (_shownSettings != section)
                {
                    await ShowSectionAsync(section).ConfigureAwait(true);
                }

                var target = SettingsBody.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => Equals(control.Tag, key));
                if (target is not null)
                {
                    FocusSetting(target);
                }
            };
            SettingsLinks.Children.Add(link);
        }
    }

    /// <summary>Keeps the page index at the top of its scrolling column.</summary>
    private void PinSettingsIndex()
    {
        var position = (TranslateTransform)SettingsIndex.RenderTransform!;
        position.Y = Math.Max(0, SettingsScroller.Offset.Y - SettingsBody.Margin.Top - SettingsIndex.Bounds.Top);
    }

    /// <summary>Moves keyboard focus to the setting and places it below the pinned index.</summary>
    /// <param name="target">The mounted setting.</param>
    private void FocusSetting(Control target)
    {
        var field = target.Focusable ? target : target.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Focusable && c.IsEffectivelyEnabled);
        field?.Focus();
        SettingsScroller.UpdateLayout();
        if (target.TranslatePoint(default, SettingsScroller) is { } at)
        {
            var clearance = (SettingsIndex.IsVisible ? SettingsIndex.Bounds.Height : 0) + 16;
            var offset = SettingsScroller.Offset;
            SettingsScroller.Offset = new Vector(offset.X, offset.Y + at.Y - clearance);
        }
    }
}
