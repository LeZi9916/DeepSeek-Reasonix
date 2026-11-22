using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Pages.Rail;

public sealed partial class ProjectRail
{
    /// <summary>Clears the filter and locates an acknowledged recent selection without opening another runtime.</summary>
    /// <param name="target">Acknowledged machine, project and transcript.</param>
    /// <param name="current">Whether a newer navigation still permits this selection.</param><returns>The visible row location.</returns>
    public async Task RevealRecentAsync(SessionOpenTarget target, Func<bool>? current = null)
    {
        await SelectScopeAsync("all").ConfigureAwait(true);
        if (current is not null && !current())
        {
            return;
        }

        var projects = target.Host.Length == 0 ? _tree : _remotes.FirstOrDefault(host => host.Name == target.Host)?.Projects ?? [];
        foreach (var project in projects.Where(project => project.Root == target.WorkspaceRoot || project.WorkspaceRoots.Contains(target.WorkspaceRoot) || project.Sessions.Any(session => session.Path == target.SessionPath)))
        {
            _collapsed.Remove(target.Host.Length == 0 ? project.Root : RemoteKey(target.Host, project.Root));
        }

        _current = target.SessionPath;
        _currentHost = target.Host;
        Rebuild();
        await Dispatcher.UIThread.InvokeAsync(
            () =>
            {
                if (current is not null && !current())
                {
                    return;
                }

                var row = _rows.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.Classes.Contains("session-open") && button.Classes.Contains("current"));
                row?.BringIntoView();
                row?.Focus();
            },
            DispatcherPriority.Loaded);
    }
}
