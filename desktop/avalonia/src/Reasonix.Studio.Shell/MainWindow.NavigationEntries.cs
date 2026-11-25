using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Workspace;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Delays opening and bridges the gap between footer and shortcuts.</summary>
    private readonly DispatcherTimer _quickLinksHover = new();

    /// <summary>Connects the icon column and the sidebar's ordered shortcut menu.</summary>
    private void InitializeNavigationEntries()
    {
        NavigationRail.Requested += async id => await GoToDestinationAsync(id).ConfigureAwait(true);
        var opening = false;
        _quickLinksHover.Tick += (_, _) =>
        {
            _quickLinksHover.Stop();
            if (opening)
            {
                QuickLinksPopup.IsOpen = WorkspaceFooter.IsPointerOver && OpenAccount.IsEffectivelyVisible;
            }
            else if (!WorkspaceFooter.IsPointerOver && !QuickLinksFrame.IsPointerOver && !QuickLinksFrame.IsKeyboardFocusWithin)
            {
                CloseQuickLinks();
            }
        };
        WorkspaceFooter.PointerEntered += (_, e) =>
        {
            if (e.Pointer.Type == PointerType.Mouse)
            {
                _quickLinksHover.Stop();
                if (OpenAccount.IsVisible && !QuickLinksPopup.IsOpen)
                {
                    Schedule(true);
                }
            }
        };
        WorkspaceFooter.PointerExited += (_, _) => Schedule(false);
        QuickLinksFrame.PointerEntered += (_, _) => _quickLinksHover.Stop();
        QuickLinksFrame.PointerExited += (_, _) => Schedule(false);
        QuickLinksPopup.Opened += (_, _) => OpenAccount.Classes.Add("expanded");
        QuickLinksPopup.Closed += (_, _) =>
        {
            _quickLinksHover.Stop();
            OpenAccount.Classes.Remove("expanded");
        };
        Deactivated += (_, _) => CloseQuickLinks();
        Closed += (_, _) => CloseQuickLinks();
        AddHandler(
            PointerPressedEvent,
            (_, e) =>
            {
                if (QuickLinksPopup.IsOpen && e.Source is Visual source
                    && !ReferenceEquals(source, WorkspaceFooter) && !WorkspaceFooter.IsVisualAncestorOf(source)
                    && !ReferenceEquals(source, QuickLinksFrame) && !QuickLinksFrame.IsVisualAncestorOf(source))
                {
                    CloseQuickLinks();
                }
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        OpenAccount.Click += (_, _) =>
        {
            _quickLinksHover.Stop();
            QuickLinksPopup.IsOpen = !QuickLinksPopup.IsOpen;
        };
        OpenAccount.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Down or Key.Up))
            {
                return;
            }

            e.Handled = true;
            _quickLinksHover.Stop();
            QuickLinksPopup.IsOpen = true;
            Dispatcher.UIThread.Post(() =>
            {
                var buttons = QuickLinksRows.Children.OfType<Button>().ToArray();
                (e.Key == Key.Up ? buttons.LastOrDefault() : buttons.FirstOrDefault())?.Focus();
            });
        };
        QuickLinksFrame.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DismissQuickLinks();
                e.Handled = true;
            }
            else if (e.Key == Key.Tab)
            {
                DismissQuickLinks();
            }
            else if (e.Key is Key.Up or Key.Down or Key.Home or Key.End)
            {
                var buttons = QuickLinksRows.Children.OfType<Button>().ToArray();
                if (buttons.Length == 0)
                {
                    return;
                }

                var current = Array.FindIndex(buttons, button => button.IsFocused);
                var next = e.Key switch
                {
                    Key.Home => 0,
                    Key.End => buttons.Length - 1,
                    Key.Up => Math.Max(0, current - 1),
                    _ => Math.Min(buttons.Length - 1, current + 1),
                };
                buttons[next].Focus();
                e.Handled = true;
            }
        };

        void Schedule(bool open)
        {
            _quickLinksHover.Stop();
            if (!open && QuickLinksFrame.IsKeyboardFocusWithin)
            {
                return;
            }

            opening = open;
            _quickLinksHover.Interval = TimeSpan.FromMilliseconds(open ? 160 : 180);
            _quickLinksHover.Start();
        }
    }

    /// <summary>Dismisses the footer menu and cancels a pending hover request.</summary>
    private void CloseQuickLinks()
    {
        _quickLinksHover.Stop();
        QuickLinksPopup.IsOpen = false;
    }

    /// <summary>Returns keyboard focus to the trigger when dismissing from inside the list.</summary>
    private void DismissQuickLinks()
    {
        var hadFocus = QuickLinksFrame.IsKeyboardFocusWithin;
        CloseQuickLinks();
        if (hadFocus)
        {
            OpenAccount.Focus();
        }
    }

    /// <summary>Applies the same destination order to the icon column and sidebar menu.</summary>
    private void DrawNavigationEntries()
    {
        NavigationRail.Draw(_workspacePreferences, Language);
        QuickLinksRows.Children.Clear();
        foreach (var item in WorkspaceDestinations.Order(_workspacePreferences))
        {
            var label = MessageCatalog.Get(item.Label, Language);
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children = { NavigationRail.Glyph(item.Glyph), new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center } },
            };
            var button = new Button { Tag = item.Id, Content = row, Classes = { "preset-flat", "workspace-quick-link" } };
            AutomationProperties.SetName(button, label);
            AutomationProperties.SetAutomationId(button, item.Id == "files" ? "action:files.open" : "action:settings.section");
            button.Click += async (_, _) =>
            {
                CloseQuickLinks();
                await GoToDestinationAsync(item.Id).ConfigureAwait(true);
            };
            QuickLinksRows.Children.Add(button);
        }

        OpenAccount.IsVisible = QuickLinksRows.Children.Count > 0;
        AutomationProperties.SetName(OpenAccount, MessageCatalog.Get("session.sidebar_shortcuts", Language));
    }

    /// <summary>Opens the actual workspace or settings destination.</summary>
    /// <param name="id">The canonical destination ID.</param>
    /// <returns>The destination's initial load.</returns>
    private async Task GoToDestinationAsync(string id)
    {
        switch (id)
        {
            case "new":
                OnNewRequested();
                return;
            case "home":
                ToggleRail();
                return;
            case "files":
                await OpenWorkbenchAsync().ConfigureAwait(true);
                return;
            case "settings":
                OpenPreferences();
                return;
        }

        var section = id switch
        {
            "memory" => "storage",
            "remote" => "network",
            _ => id,
        };
        OpenPreferences();
        SelectSettingsNav(section);
        await ShowSectionAsync(section).ConfigureAwait(true);
        var anchor = id switch
        {
            "memory" => "data.memory",
            "remote" => "settings.remote",
            _ => string.Empty,
        };
        if (anchor.Length > 0 && SettingsBody.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => Equals(control.Tag, anchor)) is { } target)
        {
            FocusSetting(target);
        }
    }
}
