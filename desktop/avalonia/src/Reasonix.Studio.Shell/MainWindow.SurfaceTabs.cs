using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Browser;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The independently open browser pages in the mounted conversation.</summary>
    private readonly List<ManualWebPreview> _browserPages = [];

    /// <summary>Appends retained browser tabs and their explicit close commands.</summary>
    private void AppendSurfaceTabs()
    {
        foreach (var page in _browserPages.ToArray())
        {
            var title = MessageCatalog.Get("workbench.browser", Language);
            var target = new WorkbenchSurfaceTarget(title, () =>
            {
                ManualBrowserHost.Content = page;
                OpenManualBrowser();
            });
            var selected = ManualBrowserHost.IsVisible && ReferenceEquals(page, ManualBrowserHost.Content);
            var group = new StackPanel { Orientation = Orientation.Horizontal, Tag = target, Classes = { "wb-tab-frame" } };
            group.Classes.Set("active", selected);
            var tab = new Button { Content = title, Classes = { "wb-tab" } };
            tab.Classes.Set("active", selected);
            AutomationProperties.SetAutomationId(tab, "action:workbench.browser-tab");
            tab.Click += (_, _) => target.Open();
            var menu = new MenuFlyout();
            Add("workbench.close_tab", "workbench.close-browser", () => CloseBrowsers([page]));
            Add("workbench.close_other_browser_tabs", "workbench.close-other-browsers", () => CloseBrowsers(_browserPages.Where(other => !ReferenceEquals(other, page)).ToArray()));
            Add("workbench.close_all_browser_tabs", "workbench.close-all-browsers", () => CloseBrowsers(_browserPages.ToArray()));
            tab.ContextFlyout = menu;
            var close = new Button { Content = "×", Width = 22, Padding = default, Classes = { "wb-tab-close" } };
            AutomationProperties.SetAutomationId(close, "action:workbench.close-browser");
            AutomationProperties.SetName(close, MessageCatalog.Get("workbench.close_tab", Language));
            close.Click += (_, _) => CloseBrowsers([page]);
            group.Children.Add(tab);
            group.Children.Add(close);
            FileTabs.Children.Add(group);

            void Add(string key, string action, Action run)
            {
                var item = new MenuItem { Header = MessageCatalog.Get(key, Language) };
                AutomationProperties.SetAutomationId(item, "action:" + action);
                item.Click += (_, _) => run();
                menu.Items.Add(item);
            }
        }
    }

    /// <summary>Closes selected browser pages and restores the surviving surface.</summary>
    /// <param name="pages">The explicit browser selection.</param>
    private void CloseBrowsers(IReadOnlyList<ManualWebPreview> pages)
    {
        var selected = pages.Any(page => ReferenceEquals(page, ManualBrowserHost.Content));
        foreach (var page in pages)
        {
            _browserPages.Remove(page);
            page.ClosePage();
            ManualBrowserHost.Release(page);
        }

        if (selected)
        {
            ManualBrowserHost.Content = _browserPages.LastOrDefault();
            if (_browserPages.Count > 0)
            {
                OpenManualBrowser();
            }
            else if (_openFile is not null)
            {
                ShowSource();
            }
            else
            {
                ShowWorkbenchLauncher();
            }
        }

        RebuildTabs();
    }

    /// <summary>A retained surface selected from a tab or its overflow entry.</summary>
    /// <param name="Title">Displayed tab title.</param><param name="Open">The tab's activation.</param>
    private sealed record WorkbenchSurfaceTarget(string Title, Action Open);
}
