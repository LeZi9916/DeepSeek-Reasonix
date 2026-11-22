using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;

namespace Reasonix.Studio.Pages.Rail;

/// <summary>A selectable conversation row with quiet actions and canonical status marks.</summary>
internal sealed partial class SessionEntry : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="SessionEntry"/> class.</summary>
    /// <param name="session">Canonical session.</param><param name="current">Whether this window displays it.</param>
    /// <param name="menu">Actions for this session.</param><param name="open">Opens the canonical path.</param>
    /// <param name="language">The interface language.</param><param name="run">Actual turn state.</param><param name="quick">Whether to offer hover actions.</param>
    /// <param name="selected">Selection state, or null outside selection mode.</param><param name="select">Toggles the selected row.</param><param name="rename">Commits a changed inline title.</param>
    internal SessionEntry(TreeSession session, bool current, MenuFlyout menu, Action open, InterfaceLanguage language, string run = "", bool quick = false, bool? selected = null, Action? select = null, Action<string>? rename = null)
    {
        InitializeComponent();
        SessionPath = session.Path;
        Title.Text = string.IsNullOrWhiteSpace(session.Title) ? session.Name : session.Title;
        Title.Classes.Set("current", current);
        Title.Classes.Set("unread", session.Unread);
        Open.Classes.Set("current", current);
        Opacity = session.Archived ? 0.45 : 1;
        Unread.IsVisible = session.Unread && run.Length == 0;
        RowFrame.Classes.Set("pinned", session.Pinned);
        RowFrame.Classes.Set("quick", quick);
        AutomationProperties.SetName(Pin, MessageCatalog.Get("session.pinned", language));
        Running.IsVisible = run is "running" or "halt";
        if (run == "halt")
        {
            Running.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("WarnBrush"));
        }

        Tail.Text = run is "running" or "halt" ? MessageCatalog.Get(run == "running" ? "common.running" : "session.needs_you", language) : SessionAge.Readout(session.LastActiveAt, DateTimeOffset.Now, language);
        if (run is "running" or "halt")
        {
            Tail.Bind(TextBlock.ForegroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(run == "running" ? "NetBrush" : "WarnInkBrush"));
        }

        if (session.LastActiveAt is >= -62135596800000 and <= 253402300799999)
        {
            ToolTip.SetTip(Tail, DateTimeOffset.FromUnixTimeMilliseconds(session.LastActiveAt.Value).ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture));
        }

        Menu.Flyout = menu;
        Open.ContextFlyout = menu;
        AutomationProperties.SetName(Open, Title.Text);
        AutomationProperties.SetHelpText(Open, session.Path);
        AutomationProperties.SetName(Menu, Title.Text);
        ToolTip.SetTip(Open, Title.Text);
        Open.Click += (_, _) =>
        {
            if (!Rename.IsVisible)
            {
                open();
            }
        };
        InitializeGestures(menu, language, selected.HasValue, rename);
        if (selected.HasValue)
        {
            Selection.IsVisible = true;
            Selection.IsChecked = selected.Value;
            Unread.IsVisible = Running.IsVisible = Menu.IsVisible = false;
            RowFrame.Classes.Set("quick", false);
            RowFrame.Classes.Set("pinned", false);
            Open.ContextFlyout = null;
            Open.Click += (_, _) => select?.Invoke();
        }

        foreach (var button in new[] { QuickPin, QuickArchive })
        {
            var item = menu.Items.OfType<MenuItem>().Single(action => AutomationProperties.GetAutomationId(action) == AutomationProperties.GetAutomationId(button));
            button.IsEnabled = item.IsEnabled;
            AutomationProperties.SetName(button, item.Header as string);
            ToolTip.SetTip(button, item.Header);
            button.Click += (_, _) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
    }
}
