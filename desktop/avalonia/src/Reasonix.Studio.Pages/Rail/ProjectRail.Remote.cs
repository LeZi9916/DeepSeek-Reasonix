using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Reasonix.Studio.Pages.Rail;

public sealed partial class ProjectRail
{
    /// <summary>Reducer-owned turn states indexed by machine and transcript.</summary>
    private readonly Dictionary<string, RailActivity> _runs = new(StringComparer.Ordinal);

    /// <summary>Last acknowledged machine projections.</summary>
    private IReadOnlyList<RemoteRailHost> _remotes = Array.Empty<RemoteRailHost>();

    /// <summary>Search terms applied to remote row titles.</summary>
    private string _remoteQuery = string.Empty;

    /// <summary>Machine owning the selected transcript.</summary>
    private string _currentHost = string.Empty;

    /// <summary>Raised to activate a remote machine, workspace and optional transcript.</summary>
    public event Action<string, string, string>? RemoteOpened;

    /// <summary>Raised for a host-scoped session mutation, with its canonical path and value.</summary>
    public event Action<string, string, string, bool>? RemoteWritten;

    /// <summary>Raised to refresh the remote session book.</summary>
    public event Action? RemoteRefreshRequested;

    /// <summary>Raised after a canonical transcript's reducer-owned activity changes.</summary>
    public event Action? ActivityChanged;

    /// <summary>Shows the current remote address book and acknowledged trees.</summary>
    /// <param name="hosts">Visible machine projections.</param>
    /// <param name="query">The current search query.</param>
    /// <param name="currentHost">Machine of the active transcript, or empty for local.</param>
    public void ShowRemotes(IReadOnlyList<RemoteRailHost> hosts, string query = "", string currentHost = "")
    {
        _remotes = hosts;
        _remoteQuery = query.Trim();
        _currentHost = currentHost;
        Rebuild();
    }

    /// <summary>Updates a transcript from an actual reducer state.</summary>
    /// <param name="host">The machine identity, empty for local.</param>
    /// <param name="path">The canonical transcript.</param>
    /// <param name="run">Running, halt, or empty for settled.</param>
    /// <param name="live">Actual turn lifetime when known.</param>
    public void ShowRun(string host, string path, string run, bool? live = null)
    {
        var state = new RailActivity(run, live ?? (run == "running"));
        if (path.Length == 0 || _runs.GetValueOrDefault(RemoteKey(host, path)) == state)
        {
            return;
        }

        _runs[RemoteKey(host, path)] = state;
        Rebuild();
        ActivityChanged?.Invoke();
    }

    /// <summary>Reads the actual running or needs-attention state for a canonical transcript.</summary>
    /// <param name="target">Machine and transcript identity.</param><returns>The known reducer state or empty.</returns>
    public string RunState(SessionOpenTarget target) => RunFor(target.Host, target.SessionPath);

    /// <summary>Reads the actual turn lifetime rather than inferring it from an open runtime.</summary>
    /// <param name="target">Canonical machine and transcript.</param><returns>Whether a turn is live.</returns>
    public bool IsLive(SessionOpenTarget target) => _runs.GetValueOrDefault(RemoteKey(target.Host, target.SessionPath))?.Live == true;

    /// <summary>Preserves both parts of a remote row identity.</summary>
    /// <param name="host">Machine name.</param><param name="path">Canonical path.</param>
    /// <returns>The collision-free composite key.</returns>
    private static string RemoteKey(string host, string path) => JsonSerializer.Serialize(new[] { host, path });

    /// <summary>Reads the actual turn state, leaving an unknown pane idle.</summary>
    /// <param name="host">Machine name.</param><param name="path">Canonical path.</param>
    /// <returns>The known run state.</returns>
    private string RunFor(string host, string path) => _runs.GetValueOrDefault(RemoteKey(host, path))?.Run ?? string.Empty;

    /// <summary>Projects remote rows through the shared pagination and fold state.</summary>
    private void RebuildRemotes()
    {
        foreach (var host in _remotes)
        {
            foreach (var workspace in host.Projects)
            {
                var hit = _remoteQuery.Length == 0 || host.Name.Contains(_remoteQuery, StringComparison.OrdinalIgnoreCase)
                    || host.Target.Contains(_remoteQuery, StringComparison.OrdinalIgnoreCase) || workspace.Name.Contains(_remoteQuery, StringComparison.OrdinalIgnoreCase);
                var sessions = RailPresenter.Sorted(workspace.Sessions).Where(session => InScope(host.Name, session))
                    .Where(s => hit || (s.Title.Length > 0 ? s.Title : s.Name).Contains(_remoteQuery, StringComparison.OrdinalIgnoreCase)).ToArray();
                if ((sessions.Length == 0 && !hit) || (_scope != "all" && sessions.Length == 0))
                {
                    continue;
                }

                var key = RemoteKey(host.Name, workspace.Root);
                var folded = _collapsed.Contains(key);
                var project = new ProjectEntry(
                    workspace,
                    folded,
                    () =>
                    {
                        if (!_collapsed.Remove(key))
                        {
                            _collapsed.Add(key);
                        }

                        Rebuild();
                    },
                    host,
                    Language);
                project.SetMenu(BuildProjectMenu(new SessionOpenTarget(host.Name, workspace.Root, string.Empty)));
                project.SetNewAction(() => Navigate(new SessionOpenTarget(host.Name, workspace.Root, string.Empty)), true, Language);
                _rows.Children.Add(project);
                if (folded && _remoteQuery.Length == 0)
                {
                    continue;
                }

                var visible = PreviewRows(host.Name, key, sessions);
                foreach (var session in visible)
                {
                    _rows.Children.Add(RemoteSessionRow(host.Name, workspace.Root, session));
                }

                AddWindowTail(host.Name, workspace, sessions.Length, visible.Length);
            }
        }
    }

    /// <summary>Builds one remote row with host-scoped mutations.</summary>
    /// <param name="host">Machine name.</param><param name="root">Project root.</param>
    /// <param name="session">Canonical transcript.</param>
    /// <returns>The native row.</returns>
    private Control RemoteSessionRow(string host, string root, TreeSession session)
    {
        var target = new SessionOpenTarget(host, session.WorkspaceRoot.Length > 0 ? session.WorkspaceRoot : root, session.Path);
        return new SessionEntry(session, _currentHost == host && RailPresenter.Same(session.Path, _current), BuildSessionMenu(target, session), () => Navigate(target), Language, RunFor(host, session.Path), QuickActions);
    }
}
