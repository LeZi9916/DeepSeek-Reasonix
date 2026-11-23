using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Browser;
using Reasonix.Studio.Shell.Workbench;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The independently open browser pages in the mounted conversation.</summary>
    private readonly List<ManualWebPreview> _browserPages = [];

    /// <summary>Appends retained browser tabs and their explicit close commands.</summary>
    /// <returns>The selected browser tab, when the browser owns the workbench.</returns>
    private WorkbenchHeaderTab? AppendSurfaceTabs()
    {
        WorkbenchHeaderTab? active = null;
        foreach (var page in _browserPages.ToArray())
        {
            var title = MessageCatalog.Get("workbench.browser", Language);
            var target = new WorkbenchSurfaceTarget(title, () =>
            {
                ManualBrowserHost.Content = page;
                OpenManualBrowser();
            });
            var selected = !WorkbenchStart.IsVisible && ManualBrowserHost.IsVisible && ReferenceEquals(page, ManualBrowserHost.Content);
            var group = new WorkbenchHeaderTab(title, "Browser", selected, Language) { Tag = target };
            if (selected)
            {
                active = group;
            }

            var tab = group.PickButton;
            AutomationProperties.SetAutomationId(tab, "action:workbench.browser-tab");
            tab.Click += (_, _) => target.Open();
            var menu = new MenuFlyout();
            menu.Items.Add(new MenuItem
            {
                Header = MessageCatalog.Get("workbench.browser_tab_named", Language, ("name", title)),
                IsEnabled = false,
            });
            Add("workbench.close_tab", "workbench.close-browser", () => CloseBrowsers([page]));
            Add("workbench.close_other_browser_tabs", "workbench.close-other-browsers", () => CloseBrowsers(_browserPages.Where(other => !ReferenceEquals(other, page)).ToArray()));
            Add("workbench.close_browser_tabs_to_the", "workbench.close-right-browsers", () => CloseBrowsers(_browserPages.SkipWhile(other => !ReferenceEquals(other, page)).Skip(1).ToArray()));
            Add("workbench.close_all_browser_tabs", "workbench.close-all-browsers", () => CloseBrowsers(_browserPages.ToArray()));
            tab.ContextFlyout = menu;
            var close = group.CloseButton;
            AutomationProperties.SetAutomationId(close, "action:workbench.close-browser");
            close.Click += (_, _) => CloseBrowsers([page]);
            FileTabs.Children.Add(group);

            void Add(string key, string action, Action run)
            {
                var item = new MenuItem { Header = MessageCatalog.Get(key, Language) };
                AutomationProperties.SetAutomationId(item, "action:" + action);
                item.Click += (_, _) => run();
                menu.Items.Add(item);
            }
        }

        return active;
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
            var next = _browserPages.LastOrDefault();
            ManualBrowserHost.Content = next;
            next?.Wake();
            if (_browserPages.Count == 0)
            {
                HideBrowser();
                RefreshWorkbenchLauncher();
            }
        }

        RebuildTabs();
    }

    /// <summary>A retained surface selected from a tab or its overflow entry.</summary>
    /// <param name="Title">Displayed tab title.</param><param name="Open">The tab's activation.</param>
    private sealed record WorkbenchSurfaceTarget(string Title, Action Open);
}
