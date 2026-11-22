using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Shell.Workspace;

public sealed partial class RecentSessionsPanel
{
    /// <summary>Updates run labels while retaining the mounted rows and their native input state.</summary>
    public void RefreshRuns()
    {
        if (!IsVisible || Rows.ItemsSource is not IEnumerable<RecentSessionHit> rows)
        {
            return;
        }

        foreach (var row in rows)
        {
            row.ShowRun(row.Session.RuntimeId.Length > 0 ? _run?.Invoke(row.Target) ?? string.Empty : string.Empty, _language);
        }
    }

    /// <summary>Projects canonical chronological rows and their local-calendar section boundaries.</summary>
    /// <param name="tree">Acknowledged page.</param><param name="host">Selected machine.</param><returns>The ordered native rows.</returns>
    private RecentSessionHit[] RecentRows(IReadOnlyList<TreeWorkspace> tree, string host)
    {
        var sorted = tree.SelectMany(workspace => workspace.Sessions.Select(session => (Workspace: workspace, Session: session)))
            .OrderByDescending(row => row.Session.LastActiveAt).ThenBy(row => row.Session.Path, StringComparer.Ordinal).ToArray();
        var now = DateTimeOffset.Now;
        var today = new DateTimeOffset(now.Date, now.Offset).ToUnixTimeMilliseconds();
        return sorted.Select((row, index) =>
        {
            var group = index == 0 || (sorted[index - 1].Session.LastActiveAt >= today) != (row.Session.LastActiveAt >= today)
                ? MessageCatalog.Get(row.Session.LastActiveAt >= today ? "common.today" : "session.earlier", _language)
                : string.Empty;
            return new RecentSessionHit(
                new SessionOpenTarget(host, row.Session.WorkspaceRoot.Length > 0 ? row.Session.WorkspaceRoot : row.Workspace.Root, row.Session.Path),
                row.Session,
                row.Workspace.Name,
                SessionAge.Readout(row.Session.LastActiveAt, now, _language),
                group);
        }).ToArray();
    }
}
