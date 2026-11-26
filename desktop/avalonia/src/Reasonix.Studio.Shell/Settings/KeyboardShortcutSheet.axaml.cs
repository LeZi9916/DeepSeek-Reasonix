using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Pages.I18n;
using System;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>The platform-specific keyboard reference.</summary>
public sealed partial class KeyboardShortcutSheet : SettingsFormView
{
    /// <summary>Initializes a new instance of the <see cref="KeyboardShortcutSheet"/> class.</summary>
    public KeyboardShortcutSheet() => InitializeComponent();

    /// <summary>Displays the shortcut table in the requested language.</summary>
    /// <param name="language">The current interface language.</param>
    internal void Open(InterfaceLanguage language)
    {
        Translate(language);
        KeyboardRows.Children.Clear();
        var primary = OperatingSystem.IsMacOS() ? "⌘" : "Ctrl+";
        foreach (var (chord, title) in new[]
        {
            (Key("F"), "settings.find_in_the_conversation"),
            (Key("K"), "settings.search_conversations"),
            (Key("B"), "session.collapse_sidebar"),
            (Key("1"), "session.show_conversation"),
            (Key("2"), "session.show_run_analysis"),
            (Key("3"), "session.show_workbench"),
            (Key("`"), "terminal.open"),
            (Key("↑") + " / " + Key("↓"), "settings.move_between_your_questions"),
            (Key("Shift+E"), "composer.refine_prompt"),
            (Key("Shift+F"), "settings.workbench_full_screen"),
            (Key("-") + " / " + Key("+") + " / " + Key("0"), "settings.make_the_interface_smaller_larger"),
            (Key("Enter"), "settings.send_when_the_send_key"),
            ("Enter / Shift+Enter", "settings.send_or_a_new_line"),
            ("Esc", "settings.close_the_open_panel"),
            ("← / → · Home", "settings.widen_a_divider_once_focused"),
        })
        {
            var chip = new Border
            {
                Classes = { "keys-cap" },
                Child = new TextBlock { Text = chord, Classes = { "keys-chord" } },
            };
            var label = new TextBlock
            {
                Text = Text(title),
                Classes = { "keys-label" },
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("156,*"), ColumnSpacing = 10, Children = { chip, label } };
            Grid.SetColumn(label, 1);
            var row = new Border { Classes = { "keys-row" }, Child = content };
            AutomationProperties.SetAutomationId(row, "class:keys-row");
            KeyboardRows.Children.Add(row);
        }

        string Key(string key) => primary + key;
    }
}
