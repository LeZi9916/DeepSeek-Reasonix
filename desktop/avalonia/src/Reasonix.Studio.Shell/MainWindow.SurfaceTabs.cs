using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Browser;
using Reasonix.Studio.Shell.Workbench;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The independently open browser pages in the mounted conversation.</summary>
    private readonly List<ManualWebPreview> _browserPages = [];

    /// <summary>Appends retained browser tabs and their explicit close commands.</summary>
    /// <param name="newSurface">Identity of a browser just opened by the reader.</param>
    /// <returns>The selected browser tab, when the browser owns the workbench.</returns>
    private WorkbenchHeaderTab? AppendSurfaceTabs(object? newSurface)
    {
        WorkbenchHeaderTab? active = null;
        foreach (var page in _browserPages.ToArray())
        {
            var title = page.TabTitle ?? MessageCatalog.Get("workbench.browser", Language);
            var target = new WorkbenchSurfaceTarget(
                title,
                () =>
                {
                    ManualBrowserHost.Content = page;
                    OpenManualBrowser();
                },
                page);
            var selected = !WorkbenchStart.IsVisible && ManualBrowserHost.IsVisible && ReferenceEquals(page, ManualBrowserHost.Content);
            var group = new WorkbenchHeaderTab(title, "Browser", selected, Language) { Tag = target };
            if (selected)
            {
                active = group;
            }

            var tab = group.PickButton;
            AutomationProperties.SetAutomationId(tab, "action:workbench.browser-tab");
            tab.Click += (_, _) => target.Open();
            tab.DoubleTapped += async (_, _) => await RenameBrowserAsync(page, title).ConfigureAwait(true);
            tab.KeyDown += async (_, e) =>
            {
                if (e.Key == Key.F2)
                {
                    e.Handled = true;
                    await RenameBrowserAsync(page, title).ConfigureAwait(true);
                }
            };
            var menu = new MenuFlyout();
            menu.Items.Add(new MenuItem
            {
                Header = MessageCatalog.Get("workbench.browser_tab_named", Language, ("name", title)),
                IsEnabled = false,
            });
            var rename = new MenuItem { Header = MessageCatalog.Get("workbench.rename_tab", Language) };
            AutomationProperties.SetAutomationId(rename, "action:workbench.rename-tab");
            rename.Click += async (_, _) => await RenameBrowserAsync(page, title).ConfigureAwait(true);
            menu.Items.Add(rename);
            Add("workbench.close_tab", "workbench.close-browser", () => CloseBrowsers([page]));
            Add("workbench.close_other_browser_tabs", "workbench.close-other-browsers", () => CloseBrowsers(_browserPages.Where(other => !ReferenceEquals(other, page)).ToArray()));
            Add("workbench.close_browser_tabs_to_the", "workbench.close-right-browsers", () => CloseBrowsers(_browserPages.SkipWhile(other => !ReferenceEquals(other, page)).Skip(1).ToArray()));
            Add("workbench.close_all_browser_tabs", "workbench.close-all-browsers", () => CloseBrowsers(_browserPages.ToArray()));
            tab.ContextFlyout = menu;
            var close = group.CloseButton;
            AutomationProperties.SetAutomationId(close, "action:workbench.close-browser");
            close.Click += async (_, _) =>
            {
                if (await group.LeaveAsync(_motionLifetime.Token).ConfigureAwait(true) && _browserPages.Contains(page))
                {
                    CloseBrowsers([page]);
                }
            };
            FileTabs.Children.Add(group);
            if (ReferenceEquals(newSurface, page) && Classes.Contains("motion"))
            {
                group.Enter(_motionLifetime.Token);
            }

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

    /// <summary>Renames a browser tab without replacing its live document.</summary>
    /// <param name="page">The current browser tab.</param><param name="title">Its display name.</param>
    /// <returns>The rename interaction.</returns>
    private async Task RenameBrowserAsync(ManualWebPreview page, string title)
    {
        var name = await AskWorkspaceAsync(MessageCatalog.Get("workbench.rename_tab", Language), title, title).ConfigureAwait(true);
        if (name is not null && _browserPages.Contains(page))
        {
            page.TabTitle = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            RebuildTabs(animateSelection: false);
            var tab = FileTabs.Children.OfType<WorkbenchHeaderTab>()
                .FirstOrDefault(candidate => candidate.Tag is WorkbenchSurfaceTarget target && ReferenceEquals(target.Canvas, page));
            tab?.PickButton.Focus();
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
    /// <param name="Canvas">The retained content for this tab.</param>
    private sealed record WorkbenchSurfaceTarget(string Title, Action Open, Control Canvas);
}
