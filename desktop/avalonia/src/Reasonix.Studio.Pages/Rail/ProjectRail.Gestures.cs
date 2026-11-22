using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Reasonix.Studio.Contracts.Tree;
using System;
using System.Linq;

namespace Reasonix.Studio.Pages.Rail;

public sealed partial class ProjectRail
{
    /// <summary>The native equivalent of the reference frontend's project drag payload.</summary>
    private static readonly DataFormat<string> s_workspaceDragFormat = DataFormat.CreateStringApplicationFormat("reasonix-workspace");

    /// <summary>Connects native file drops without treating text or project drags as folders.</summary>
    private void InitializeGestures()
    {
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, (_, e) =>
        {
            e.DragEffects = e.DataTransfer.Formats.Contains(DataFormat.File) && !e.DataTransfer.Formats.Contains(DataFormat.Text)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        });
        DragDrop.AddDropHandler(this, (_, e) =>
        {
            if (!e.DataTransfer.Formats.Contains(DataFormat.File) || e.DataTransfer.Formats.Contains(DataFormat.Text))
            {
                return;
            }

            e.Handled = true;
            var files = e.DataTransfer.TryGetFiles();
            if (files?.Length != 1)
            {
                RequestAction(new WorkspaceAction("workspace.drop-error", new SessionOpenTarget(string.Empty, string.Empty, string.Empty), Value: "session.drop_one_folder_at_a"));
            }
            else if (files[0] is IStorageFolder && files[0].TryGetLocalPath() is { Length: > 0 } path)
            {
                RequestAction(new WorkspaceAction("workspace.add-path", new SessionOpenTarget(string.Empty, path, string.Empty)));
            }
            else
            {
                RequestAction(new WorkspaceAction("workspace.drop-error", new SessionOpenTarget(string.Empty, string.Empty, string.Empty), Value: "session.the_local_path_is_unavailable"));
            }
        });
    }

    /// <summary>Lets remembered local projects name their canonical placement by either edge.</summary>
    /// <param name="project">Native row.</param><param name="workspace">Canonical local project.</param>
    private void ConfigureProjectDrag(ProjectEntry project, TreeWorkspace workspace)
    {
        if (!workspace.Remembered || _query.Length > 0)
        {
            return;
        }

        var fold = project.FindControl<Button>("Fold")!;
        var marker = project.FindControl<Border>("DropMarker")!;
        PointerPressedEventArgs? pressed = null;
        Point origin = default;
        fold.AddHandler(
            PointerPressedEvent,
            (_, e) =>
        {
            if (e.GetCurrentPoint(fold).Properties.IsLeftButtonPressed)
            {
                pressed = e;
                origin = e.GetPosition(fold);
            }
        },
            RoutingStrategies.Tunnel);
        fold.AddHandler(PointerReleasedEvent, (_, _) => pressed = null, RoutingStrategies.Tunnel);
        fold.AddHandler(
            PointerMovedEvent,
            async (_, e) =>
        {
            if (pressed is not { } start || !e.GetCurrentPoint(fold).Properties.IsLeftButtonPressed || (Math.Abs(e.GetPosition(fold).X - origin.X) < 4 && Math.Abs(e.GetPosition(fold).Y - origin.Y) < 4))
            {
                return;
            }

            pressed = null;
            e.Handled = true;
            using var data = new DataTransfer();
            data.Add(DataTransferItem.Create(s_workspaceDragFormat, workspace.Root));
            try
            {
                project.Opacity = 0.5;
                await DragDrop.DoDragDropAsync(start, data, DragDropEffects.Move).ConfigureAwait(true);
            }
            finally
            {
                project.Opacity = workspace.Missing ? 0.5 : 1;
                marker.IsVisible = false;
            }
        },
            RoutingStrategies.Tunnel);
        DragDrop.SetAllowDrop(project, true);
        DragDrop.AddDragOverHandler(project, (_, e) =>
        {
            if (!AcceptsProjectDrag(e))
            {
                return;
            }

            e.Handled = true;
            e.DragEffects = DragDropEffects.Move;
            marker.VerticalAlignment = e.GetPosition(project).Y < fold.Bounds.Height / 2 ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            marker.IsVisible = true;
        });
        DragDrop.AddDragLeaveHandler(project, (_, _) => marker.IsVisible = false);
        DragDrop.AddDropHandler(project, (_, e) =>
        {
            marker.IsVisible = false;
            if (!AcceptsProjectDrag(e))
            {
                return;
            }

            e.Handled = true;
            var from = e.DataTransfer.TryGetValue(s_workspaceDragFormat)!;
            var order = _tree.Where(project => project.Remembered).Select(project => project.Root).ToArray();
            var at = Array.IndexOf(order, workspace.Root);
            var before = e.GetPosition(project).Y < fold.Bounds.Height / 2 ? workspace.Root : order.ElementAtOrDefault(at + 1) ?? string.Empty;
            var source = Array.IndexOf(order, from);
            if (before != from && (before.Length == 0 ? source != order.Length - 1 : order.ElementAtOrDefault(source + 1) != before))
            {
                RequestAction(new WorkspaceAction("workspace.place", new SessionOpenTarget(string.Empty, from, string.Empty), Value: before));
            }
        });
    }

    /// <summary>Checks the complete remembered list because filtered rows cannot define placements.</summary>
    /// <param name="e">Native drag payload.</param><returns>Whether this drag names a remembered local root.</returns>
    private bool AcceptsProjectDrag(DragEventArgs e) => _query.Length == 0
        && e.DataTransfer.TryGetValue(s_workspaceDragFormat) is { } root
        && _tree.Any(project => project.Remembered && project.Root == root);

    /// <summary>Uses canonical archived peers, regardless of pin presentation or the visible page.</summary>
    /// <param name="target">Canonical row identity.</param><param name="session">Canonical session.</param>
    /// <param name="up">Whether to move to the previous peer.</param><returns>Whether a peer exists in that direction.</returns>
    private bool CanMoveSession(SessionOpenTarget target, TreeSession session, bool up)
    {
        if (target.Host.Length > 0 || _query.Length > 0)
        {
            return false;
        }

        var workspace = _tree.FirstOrDefault(workspace => workspace.Sessions.Any(peer => peer.Path == target.SessionPath));
        var peers = workspace?.Sessions.Where(peer => peer.Archived == session.Archived).ToArray() ?? Array.Empty<TreeSession>();
        var at = Array.FindIndex(peers, peer => peer.Path == target.SessionPath);
        return up ? at > 0 : at >= 0 && at < peers.Length - 1;
    }
}
