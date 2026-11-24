using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Shell.Session;

/// <summary>Native tabs that focus retained panes and expose explicit close operations.</summary>
internal sealed partial class SessionTabs : UserControl
{
    /// <summary>Retains the current projection while its labels and states remain unchanged.</summary>
    private string _snapshot = string.Empty;

    /// <summary>Initializes a new instance of the <see cref="SessionTabs"/> class.</summary>
    public SessionTabs() => InitializeComponent();

    /// <summary>Raised to focus the selected retained pane.</summary>
    internal event Action<SessionPaneState>? FocusRequested;

    /// <summary>Raised to explicitly close the selected panes.</summary>
    internal event Action<SessionPaneState[]>? CloseRequested;

    /// <summary>Raised to rename a local pane's canonical conversation.</summary>
    internal event Action<SessionPaneState>? RenameRequested;

    /// <summary>Projects canonical pane identities and activity without recreating their engines.</summary>
    /// <param name="panes">Retained panes.</param><param name="active">Focused pane.</param>
    /// <param name="language">Interface language.</param>
    internal void Show(IReadOnlyList<SessionPaneState> panes, SessionPaneState? active, InterfaceLanguage language)
    {
        IsVisible = panes.Count > 1;
        var snapshot = string.Join("\n", panes.Select(p => $"{p.Engine.GetHashCode()}:{p.Status.Label}:{p.Engine.State.Running}:{p.Unread}:{ReferenceEquals(p, active)}"));
        if (_snapshot == snapshot)
        {
            return;
        }

        _snapshot = snapshot;
        Tabs.Children.Clear();
        foreach (var pane in panes)
        {
            var running = pane.Engine.State.Running;
            var pip = new Border { Width = 6, Height = 6, CornerRadius = new Avalonia.CornerRadius(3), VerticalAlignment = VerticalAlignment.Center };
            pip.Classes.Add("pane-pip");
            pip.Classes.Set("running", running);
            pip.Classes.Set("unread", !running && pane.Unread);
            pip.Bind(Border.BackgroundProperty, pip.GetResourceObservable(running ? "NetBrush" : pane.Unread ? "AccentInkBrush" : "GhostBrush"));
            var title = new TextBlock
            {
                Text = pane.Status.Label.Length > 0 ? pane.Status.Label : MessageCatalog.Get("common.new_session", language),
                MaxWidth = 180,
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 7 };
            content.Children.Add(pip);
            content.Children.Add(title);
            Grid.SetColumn(title, 1);
            var button = new Button { Classes = { "pane-tab" }, Content = content };
            button.Classes.Set("current", ReferenceEquals(pane, active));
            AutomationProperties.SetAutomationId(button, "action:pane.activate");
            AutomationProperties.SetName(button, pane.Status.Label);
            ToolTip.SetTip(button, pane.Host.Length > 0 ? pane.Host + " · " + pane.Status.WorkspaceRoot : pane.Status.WorkspaceRoot);
            button.Click += (_, _) => FocusRequested?.Invoke(pane);
            if (pane.Host.Length == 0)
            {
                button.DoubleTapped += (_, _) => RenameRequested?.Invoke(pane);
            }

            var menu = new MenuFlyout();
            if (pane.Host.Length == 0)
            {
                Add("common.renamed", () => RenameRequested?.Invoke(pane));
            }

            Add("workbench.close_tab", () => CloseRequested?.Invoke([pane]));
            Add("app.close_the_others_n", () => CloseRequested?.Invoke(panes.Where(p => p != pane).ToArray()), panes.Count - 1);
            Add("app.close_all_n", () => CloseRequested?.Invoke(panes.ToArray()), panes.Count);
            button.ContextFlyout = menu;
            var close = new Button { Content = "×" };
            close.Classes.Add("pane-close");
            close.Classes.Set("current", ReferenceEquals(pane, active));
            AutomationProperties.SetAutomationId(close, "action:pane.close");
            AutomationProperties.SetName(close, MessageCatalog.Get("workbench.close_tab", language));
            close.Click += (_, _) => CloseRequested?.Invoke([pane]);
            var group = new StackPanel { Orientation = Orientation.Horizontal, Classes = { "pane-tab-group" }, Children = { button, close } };
            Tabs.Children.Add(group);
            button.KeyDown += (_, e) =>
            {
                if (e.Key is not (Key.Left or Key.Right or Key.Home or Key.End))
                {
                    return;
                }

                var index = panes.ToList().IndexOf(pane);
                index = e.Key == Key.Home ? 0 : e.Key == Key.End ? panes.Count - 1 : (index + (e.Key == Key.Left ? -1 : 1) + panes.Count) % panes.Count;
                FocusRequested?.Invoke(panes[index]);
                e.Handled = true;
            };

            void Add(string key, Action action, int count = -1)
            {
                var item = new MenuItem { Header = count < 0 ? MessageCatalog.Get(key, language) : MessageCatalog.Get(key, language, ("n", count.ToString(System.Globalization.CultureInfo.CurrentCulture))), IsEnabled = count != 0 };
                item.Click += (_, _) => action();
                menu.Items.Add(item);
            }
        }
    }
}
