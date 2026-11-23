using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Host;
using Reasonix.Studio.Shell.Workspace;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Orders and hides the destinations shared by the icon rail and the sidebar.</summary>
public sealed partial class NavigationEntriesSettings : SettingsFormView
{
    /// <summary>Initializes a new instance of the <see cref="NavigationEntriesSettings"/> class.</summary>
    public NavigationEntriesSettings() => InitializeComponent();

    /// <summary>Opens the local destination arrangement editor.</summary>
    /// <param name="language">The interface language.</param>
    /// <param name="preferences">The current arrangement.</param>
    /// <param name="commit">Applies the updated order and hidden IDs.</param>
    internal void Open(InterfaceLanguage language, WorkspacePreferences preferences, Action<IReadOnlyList<string>, IReadOnlyList<string>> commit)
    {
        Translate(language);
        var order = WorkspaceDestinations.Order(preferences, includeHidden: true).ToList();
        var hidden = (preferences.HiddenNavigation ?? []).ToHashSet(StringComparer.Ordinal);
        var changed = (preferences.NavigationOrder ?? []).Count > 0 || hidden.Count > 0;
        Reset.Click += (_, _) =>
        {
            order = WorkspaceDestinations.All.ToList();
            hidden.Clear();
            changed = false;
            commit([], []);
            Draw();
        };
        Draw();

        void Draw()
        {
            NavigationRows.Children.Clear();
            foreach (var destination in order)
            {
                var index = order.IndexOf(destination);
                var label = Text(destination.Label);
                var toggle = new ToggleSwitch { Tag = destination.Id, IsChecked = !hidden.Contains(destination.Id) };
                AutomationProperties.SetAutomationId(toggle, "action:appearance.nav-entry-visible");
                AutomationProperties.SetName(toggle, Text("settings.show_name", ("name", label)));
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                buttons.Children.Add(Move("up", "↑", -1));
                buttons.Children.Add(Move("down", "↓", 1));
                buttons.Children.Add(toggle);
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    ColumnSpacing = 12,
                    Children = { new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, buttons },
                };
                Grid.SetColumn(buttons, 1);
                NavigationRows.Children.Add(new Border { Child = row, Classes = { "navigation-entry" } });
                toggle.IsCheckedChanged += (_, _) =>
                {
                    if (toggle.IsChecked == true)
                    {
                        hidden.Remove(destination.Id);
                    }
                    else
                    {
                        hidden.Add(destination.Id);
                    }

                    Persist();
                };

                Button Move(string direction, string icon, int offset)
                {
                    var button = new Button
                    {
                        Content = icon,
                        Tag = destination.Id + ":" + direction,
                        Classes = { "preset-act", "navigation-move" },
                        IsEnabled = index + offset >= 0 && index + offset < order.Count,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    AutomationProperties.SetAutomationId(button, "action:appearance.nav-entry-move");
                    AutomationProperties.SetName(button, Text("settings.move_" + direction + "_name", ("name", label)));
                    button.Click += (_, _) =>
                    {
                        var next = index + offset;
                        (order[index], order[next]) = (order[next], order[index]);
                        Persist();
                        Draw();
                    };
                    return button;
                }
            }

            Reset.IsEnabled = changed;
        }

        void Persist()
        {
            changed = true;
            Reset.IsEnabled = true;
            commit(order.Select(item => item.Id).ToArray(), hidden.ToArray());
        }
    }
}
