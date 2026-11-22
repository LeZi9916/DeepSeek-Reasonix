using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.I18n;

namespace Reasonix.Studio.Pages.Rail;

public sealed partial class ProjectRail
{
    /// <summary>Draws the reference group boundary without introducing a selectable command.</summary>
    /// <returns>The native separator.</returns>
    private static Separator MenuDivider()
    {
        var divider = new Separator { Margin = new Thickness(7, 5), Height = 1, MinHeight = 0, Padding = default };
        divider.Bind(BackgroundProperty, new DynamicResourceExtension("HairBrush"));
        return divider;
    }

    /// <summary>Retains the same native menu presenter with the reference popup surface.</summary>
    /// <returns>The bounded native popup.</returns>
    private MenuFlyout WorkspaceMenu() => new() { FlyoutPresenterTheme = this.FindResource("WorkspaceMenuPresenterTheme") as ControlTheme };

    /// <summary>Places earlier copies in the same reference lifecycle group as move and archive.</summary>
    /// <param name="session">Acknowledged copies and versions.</param><returns>The native disclosure action.</returns>
    private MenuItem KeptMenuItem(TreeSession session)
    {
        var item = MenuActionItem("session.kept", MessageCatalog.Get("session.earlier_versions_n", Language, ("n", Format.Count(session.Versions.Count + session.Copies.Count, Language))));
        AutomationProperties.SetAutomationId(item, "action:session.kept");
        item.Click += (_, _) =>
        {
            if (!_spread.Remove(session.Path))
            {
                _spread.Add(session.Path);
            }

            Rebuild();
        };
        return item;
    }

    /// <summary>Keeps both export formats on the existing canonical action row.</summary>
    /// <param name="menu">Owning popup.</param><param name="target">Canonical session target.</param><param name="session">Acknowledged metadata.</param>
    /// <returns>The grouped export controls.</returns>
    private MenuItem ExportMenuItem(MenuFlyout menu, SessionOpenTarget target, TreeSession session)
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 9 };
        header.Children.Add(new TextBlock { Text = MessageCatalog.Get("session.export_session", Language), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        var item = MenuActionItem("session.export", string.Empty);
        item.Header = header;
        AutomationProperties.SetAutomationId(item, "class:session-export");
        foreach (var (action, label, column) in new[] { ("session.export-md", "Markdown", 1), ("session.export", "JSON", 2) })
        {
            var button = new Button { Content = label, MinHeight = 0, Height = 20, Padding = new Thickness(7, 0), BorderThickness = default, CornerRadius = new CornerRadius(999), FontSize = 11 };
            button.Bind(BackgroundProperty, new DynamicResourceExtension("OverlayBrush"));
            button.Bind(ForegroundProperty, new DynamicResourceExtension("MutedBrush"));
            AutomationProperties.SetAutomationId(button, "action:" + action);
            AutomationProperties.SetName(button, MessageCatalog.Get("session.export_session", Language) + " · " + label);
            Grid.SetColumn(button, column);
            button.Click += (_, e) =>
            {
                e.Handled = true;
                menu.Hide();
                RequestAction(new WorkspaceAction(action, target, session));
            };
            button.AddHandler(
                KeyDownEvent,
                (_, e) =>
                {
                    if (e.Key is Key.Enter or Key.Space)
                    {
                        e.Handled = true;
                        menu.Hide();
                        RequestAction(new WorkspaceAction(action, target, session));
                    }
                },
                RoutingStrategies.Tunnel);
            header.Children.Add(button);
        }

        item.Click += (_, _) =>
        {
            menu.Hide();
            RequestAction(new WorkspaceAction("session.export-md", target, session));
        };
        return item;
    }
}
