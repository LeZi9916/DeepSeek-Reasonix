using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Pages.Rail;

public sealed partial class ProjectRail
{
    /// <summary>Raised to discover the owning host's currently installed editor catalog.</summary>
    public event Func<Task<IReadOnlyList<WorkspaceOpenTarget>>>? OpenWithRequested;

    /// <summary>Updates program choices each time the anchored menu opens.</summary>
    /// <param name="menu">Anchored project menu.</param><param name="target">Canonical local project.</param><param name="missing">Whether its folder is gone.</param>
    private void DiscoverProjectEditors(MenuFlyout menu, SessionOpenTarget target, bool missing)
    {
        var generation = 0;
        var dynamic = new List<MenuItem>();
        menu.Closed += (_, _) => generation++;
        menu.Opened += async (_, _) =>
        {
            var read = ++generation;
            foreach (var item in dynamic)
            {
                menu.Items.Remove(item);
            }

            dynamic.Clear();
            if (OpenWithRequested is null)
            {
                return;
            }

            try
            {
                var editors = await OpenWithRequested.Invoke().ConfigureAwait(true);
                if (read != generation || !menu.IsOpen)
                {
                    return;
                }

                foreach (var editor in editors.Where(editor => editor.Id != "file-manager" && (editor.Available || editor.Why == "inside_project")))
                {
                    var item = MenuActionItem("workspace.open-with", MessageCatalog.Get("common.open_with_name", Language, ("name", editor.Name)));
                    item.IsEnabled = !missing && editor.Available;
                    AutomationProperties.SetAutomationId(item, "action:workspace.open-with");
                    if (!editor.Available)
                    {
                        ToolTip.SetTip(item, MessageCatalog.Get("common.inside_a_project_folder", Language));
                    }

                    item.Click += (_, _) => RequestAction(new WorkspaceAction("workspace.open-with", target, Value: editor.Id));
                    dynamic.Add(item);
                    menu.Items.Insert(menu.Items.Count - 2, item);
                }
            }
            catch (Exception)
            {
            }
        };
    }

    /// <summary>Offers remote address-book actions using visible machine neighbors.</summary>
    /// <param name="target">Canonical remote project.</param><returns>The anchored remote menu.</returns>
    private MenuFlyout BuildRemoteProjectMenu(SessionOpenTarget target)
    {
        var machine = _remotes.First(host => host.Name == target.Host);
        var position = _remotes.ToList().FindIndex(host => host.Name == target.Host);
        var menu = WorkspaceMenu();
        var name = machine.Projects.First(project => project.Root == target.WorkspaceRoot).Name;
        menu.Items.Add(MenuHeader(name, target.Host + " · " + target.WorkspaceRoot));
        var refresh = MenuActionItem("remote.read", MessageCatalog.Get("remote.refresh_the_sessions_on_name", Language, ("name", target.Host)));
        AutomationProperties.SetAutomationId(refresh, "action:remote.read");
        refresh.Click += (_, _) => RemoteRefreshRequested?.Invoke();
        menu.Items.Add(refresh);
        foreach (var direction in new[] { -1, 1 })
        {
            var item = MenuActionItem(direction < 0 ? "workspace.move-up" : "workspace.move-down", MessageCatalog.Get(direction < 0 ? "common.move_name_up" : "common.move_name_down", Language, ("name", target.Host)));
            item.IsEnabled = position + direction >= 0 && position + direction < _remotes.Count;
            AutomationProperties.SetAutomationId(item, "action:remote-host.move");
            var before = direction < 0 ? _remotes.ElementAtOrDefault(position - 1)?.Name ?? string.Empty : _remotes.ElementAtOrDefault(position + 2)?.Name ?? string.Empty;
            item.Click += (_, _) => RequestAction(new WorkspaceAction("remote-host.move", target, Value: before));
            menu.Items.Add(item);
        }

        var project = machine.Projects.First(project => project.Root == target.WorkspaceRoot);
        if (machine.SavedRoots.Any(root => root == project.Root || project.WorkspaceRoots.Contains(root)))
        {
            var remove = MenuActionItem("workspace.remove", MessageCatalog.Get("common.remove_from_the_list", Language));
            AutomationProperties.SetAutomationId(remove, "action:remote-host.remove");
            remove.Click += (_, _) => RequestAction(new WorkspaceAction("workspace.remove", target));
            menu.Items.Add(remove);
        }

        return menu;
    }
}
