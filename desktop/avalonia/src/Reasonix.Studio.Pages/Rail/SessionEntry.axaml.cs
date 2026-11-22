using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.I18n;
using System;

namespace Reasonix.Studio.Pages.Rail;

/// <summary>A selectable conversation row with quiet actions and canonical status marks.</summary>
internal sealed partial class SessionEntry : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="SessionEntry"/> class.</summary>
    /// <param name="session">Canonical session.</param><param name="current">Whether this window displays it.</param>
    /// <param name="menu">Actions for this session.</param><param name="open">Opens the canonical path.</param>
    /// <param name="language">The interface language.</param><param name="run">Actual turn state.</param>
    internal SessionEntry(TreeSession session, bool current, MenuFlyout menu, Action open, InterfaceLanguage language, string run = "")
    {
        InitializeComponent();
        Title.Text = string.IsNullOrWhiteSpace(session.Title) ? session.Name : session.Title;
        Title.Classes.Set("current", current);
        Title.Classes.Set("unread", session.Unread);
        Open.Classes.Set("current", current);
        Opacity = session.Archived ? 0.45 : 1;
        Unread.IsVisible = session.Unread && run.Length == 0;
        Pin.IsVisible = false;
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
        AutomationProperties.SetName(Menu, Title.Text);
        ToolTip.SetTip(Open, Title.Text);
        Open.Click += (_, _) => open();
    }
}
