using Avalonia.Controls;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.Shell.Workspace;
using System.Linq;
using System.Text.Json.Nodes;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The currently opened canonical project destination dialog.</summary>
    private WorkspaceMoveDialog? _workspaceMove;

    /// <summary>Chooses from known remembered projects and keeps refusal feedback inside the move dialog.</summary>
    /// <param name="action">The local transcript selected by the native menu.</param>
    private void OpenWorkspaceMove(WorkspaceAction action)
    {
        if ((_launchOwner ?? _engine) is not { } owner || ScaledWindow.Child is not Grid host)
        {
            return;
        }

        if (_workspaceMove is null)
        {
            _workspaceMove = new WorkspaceMoveDialog { ZIndex = 1100 };
            host.Children.Add(_workspaceMove);
            _workspaceMove.AddRequested += () => Projects.FindControl<Button>("AddProject")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        }

        var projects = Projects.LocalProjects.Where(project => project.Remembered && project.Root != action.Target.WorkspaceRoot).ToArray();
        _workspaceMove.Open(
            projects,
            action.Session?.Title ?? string.Empty,
            async project =>
            {
                var result = await owner.Pages.WorkspaceActionAsync("sessions/move-to", new JsonObject { ["path"] = action.Target.SessionPath, ["target"] = project.Root }, _closing.Token).ConfigureAwait(true);
                await LoadTreeAsync().ConfigureAwait(true);
                return (bool?)result?["originalRemoved"] == true;
            },
            Language);
    }
}
