using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Serializes native folder drops with the sidebar's existing project picker.</summary>
    private bool _addingSidebarProject;

    /// <summary>Registers and opens the chosen folder through the shared navigation coordinator.</summary>
    /// <param name="path">Local folder chosen by picker or native drop.</param><returns>The acknowledged navigation.</returns>
    private async Task AddSidebarProjectAsync(string path)
    {
        if (_addingSidebarProject)
        {
            return;
        }

        _addingSidebarProject = true;
        try
        {
            await ActivateProjectAsync(path, true).ConfigureAwait(true);
        }
        finally
        {
            _addingSidebarProject = false;
        }
    }

    /// <summary>Dispatches direct edits and placements while retaining canonical acknowledgement.</summary>
    /// <param name="action">Canonical input operation.</param><returns>Whether this operation was handled.</returns>
    private async Task<bool> RunSidebarGestureAsync(WorkspaceAction action)
    {
        var owner = _launchOwner ?? _engine;
        if (owner is null)
        {
            return false;
        }

        switch (action.Action)
        {
            case "workspace.drop-error":
                throw new InvalidOperationException(MessageCatalog.Get(action.Value, Language));
            case "workspace.add-path":
                await AddSidebarProjectAsync(action.Target.WorkspaceRoot).ConfigureAwait(true);
                return true;
            case "workspace.place":
                await owner.Pages.WorkspaceActionAsync("workspaces/move", new JsonObject { ["path"] = action.Target.WorkspaceRoot, ["before"] = action.Value }, _closing.Token).ConfigureAwait(true);
                break;
            case "session.rename" when action.Target.Host.Length == 0 && !string.IsNullOrWhiteSpace(action.Value):
                await owner.Pages.WorkspaceActionAsync("sessions/rename", new JsonObject { ["path"] = action.Target.SessionPath, ["title"] = action.Value.Trim() }, _closing.Token).ConfigureAwait(true);
                break;
            default:
                return false;
        }

        await LoadTreeAsync().ConfigureAwait(true);
        return true;
    }
}
