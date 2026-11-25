using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.Composer;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Applies existing canonical operations while retaining every refused row.</summary>
    /// <param name="action">Archive, restore or delete.</param><param name="rows">Selected displayed local sessions.</param>
    /// <returns>The paths that were not acknowledged.</returns>
    private async Task<IReadOnlyList<string>> RunSidebarBatchAsync(string action, IReadOnlyList<TreeSession> rows)
    {
        var engine = _launchOwner ?? _engine;
        if (engine is null)
        {
            return rows.Select(row => row.Path).ToArray();
        }

        if (action == "delete" && await AskWorkspaceAsync(MessageCatalog.Get("session.delete_n_sessions", Language, ("n", Format.Count(rows.Count, Language))), string.Join(Environment.NewLine, rows.Select(row => row.Title.Length > 0 ? row.Title : row.Name))).ConfigureAwait(true) is null)
        {
            return rows.Select(row => row.Path).ToArray();
        }

        var failed = new List<string>();
        Exception? first = null;
        foreach (var row in rows)
        {
            try
            {
                var pane = _navigation?.Panes.FirstOrDefault(pane => pane.Host.Length == 0 && pane.Status.SessionPath == row.Path);
                if (pane?.Engine.State.Running == true || Projects.IsLive(new SessionOpenTarget(string.Empty, row.WorkspaceRoot, row.Path)))
                {
                    throw new InvalidOperationException(MessageCatalog.Get("session.selected_sessions_are_running_stop", Language));
                }

                if (action == "delete")
                {
                    if (pane is not null)
                    {
                        await ClosePanesAsync(new[] { pane }).ConfigureAwait(true);
                    }
                    else if (row.RuntimeId.Length > 0)
                    {
                        await engine.Pages.WorkspaceRuntimeActionAsync(row.RuntimeId, true, _closing.Token).ConfigureAwait(true);
                    }

                    await engine.Pages.WorkspaceActionAsync("sessions/remove", new JsonObject { ["path"] = row.Path }, _closing.Token).ConfigureAwait(true);
                    var root = pane?.Status.WorkspaceRoot ?? Projects.SearchEntries.FirstOrDefault(entry => entry.Target.Host.Length == 0 && entry.Session.Path == row.Path).Target?.WorkspaceRoot ?? row.WorkspaceRoot;
                    _composer.WriteDraft(DraftStore.KeyFor(pane?.Engine.DraftHost ?? engine.DraftHost, root, row.Path), string.Empty);
                }
                else
                {
                    await engine.ArchiveSessionAsync(row.Path, action == "archive", _closing.Token).ConfigureAwait(true);
                }
            }
            catch (Exception error)
            {
                failed.Add(row.Path);
                first ??= error;
            }
        }

        await LoadTreeAsync().ConfigureAwait(true);
        if (first is not null)
        {
            ShowRefusal(first);
        }

        return failed;
    }
}
