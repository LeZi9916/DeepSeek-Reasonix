using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Contracts.Tree;
using System;

namespace Reasonix.Studio.Pages.Rail;

/// <summary>A selectable conversation row with quiet actions and canonical status marks.</summary>
internal sealed partial class SessionEntry : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="SessionEntry"/> class.</summary>
    /// <param name="session">Canonical session.</param>
    /// <param name="current">Whether this window displays the session.</param>
    /// <param name="menu">Actions for this session.</param>
    /// <param name="open">Opens the canonical path.</param>
    internal SessionEntry(TreeSession session, bool current, MenuFlyout menu, Action open)
    {
        InitializeComponent();
        Title.Text = string.IsNullOrWhiteSpace(session.Title) ? session.Name : session.Title;
        Title.Classes.Set("current", current);
        Open.Classes.Set("current", current);
        Opacity = session.Archived ? 0.45 : 1;
        Unread.IsVisible = session.Unread;
        Pin.IsVisible = session.Pinned && !session.Unread;
        Running.IsVisible = session.RuntimeId.Length > 0 && !session.Pinned && !session.Unread;
        Menu.Flyout = menu;
        Open.ContextFlyout = menu;
        AutomationProperties.SetName(Open, Title.Text);
        AutomationProperties.SetName(Menu, Title.Text);
        ToolTip.SetTip(Open, Title.Text);
        Open.Click += (_, _) => open();
    }
}
