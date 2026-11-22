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
    private readonly Dictionary<string, string> _runs = new(StringComparer.Ordinal);

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
    public void ShowRun(string host, string path, string run)
    {
        if (path.Length == 0 || RunFor(host, path) == run)
        {
            return;
        }

        _runs[RemoteKey(host, path)] = run;
        Rebuild();
    }

    /// <summary>Preserves both parts of a remote row identity.</summary>
    /// <param name="host">Machine name.</param><param name="path">Canonical path.</param>
    /// <returns>The collision-free composite key.</returns>
    private static string RemoteKey(string host, string path) => JsonSerializer.Serialize(new[] { host, path });

    /// <summary>Reads the actual turn state, leaving an unknown pane idle.</summary>
    /// <param name="host">Machine name.</param><param name="path">Canonical path.</param>
    /// <returns>The known run state.</returns>
    private string RunFor(string host, string path) => _runs.GetValueOrDefault(RemoteKey(host, path), string.Empty);

    /// <summary>Projects remote rows through the shared pagination and fold state.</summary>
    private void RebuildRemotes()
    {
        foreach (var host in _remotes)
        {
            foreach (var workspace in host.Projects)
            {
                var hit = _remoteQuery.Length == 0 || host.Name.Contains(_remoteQuery, StringComparison.OrdinalIgnoreCase)
                    || host.Target.Contains(_remoteQuery, StringComparison.OrdinalIgnoreCase) || workspace.Name.Contains(_remoteQuery, StringComparison.OrdinalIgnoreCase);
                var sessions = RailPresenter.Sorted(workspace.Sessions).Where(MatchesScope)
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
                var menu = new MenuFlyout();
                var start = new MenuItem { Header = MessageCatalog.Get("common.new_session", Language) };
                start.Click += (_, _) => RemoteOpened?.Invoke(host.Name, workspace.Root, string.Empty);
                var refresh = new MenuItem { Header = MessageCatalog.Get("common.reload", Language) };
                refresh.Click += (_, _) => RemoteRefreshRequested?.Invoke();
                menu.Items.Add(start);
                menu.Items.Add(refresh);
                project.ContextFlyout = menu;
                _rows.Children.Add(project);
                if (folded && _remoteQuery.Length == 0)
                {
                    continue;
                }

                var visible = _expanded.Contains(key) || _remoteQuery.Length > 0 ? sessions.Length : 5;
                foreach (var session in sessions.Take(visible))
                {
                    _rows.Children.Add(RemoteSessionRow(host.Name, workspace.Root, session));
                }

                if (sessions.Length > visible)
                {
                    var more = new Button
                    {
                        Content = MessageCatalog.Get(MessageKey.Common.ShowMoreNLeft, Language, ("n", Format.Count(sessions.Length - visible, Language))),
                        Classes = { "rail-button", "session-more" },
                    };
                    AutomationProperties.SetAutomationId(more, "action:session.more");
                    more.Click += (_, _) =>
                    {
                        _expanded.Add(key);
                        Rebuild();
                    };
                    _rows.Children.Add(more);
                }
            }
        }
    }

    /// <summary>Builds one remote row with host-scoped mutations.</summary>
    /// <param name="host">Machine name.</param><param name="root">Project root.</param>
    /// <param name="session">Canonical transcript.</param>
    /// <returns>The native row.</returns>
    private Control RemoteSessionRow(string host, string root, TreeSession session)
    {
        var menu = new MenuFlyout();
        void Open() => RemoteOpened?.Invoke(host, session.WorkspaceRoot.Length > 0 ? session.WorkspaceRoot : root, session.Path);
        var open = new MenuItem { Header = MessageCatalog.Get("common.open", Language) };
        open.Click += (_, _) => Open();
        menu.Items.Add(open);
        foreach (var (action, key, value) in new[]
        {
            ("pin", session.Pinned ? "common.unpin" : "common.pin_session", !session.Pinned),
            ("archive", session.Archived ? "common.unarchive" : "common.archive_session", !session.Archived),
            ("viewed", "common.mark_as_read", true),
        })
        {
            if (action == "viewed" && !session.Unread)
            {
                continue;
            }

            var item = new MenuItem { Header = MessageCatalog.Get(key, Language) };
            item.Click += (_, _) => RemoteWritten?.Invoke(host, session.Path, action, value);
            menu.Items.Add(item);
        }

        return new SessionEntry(session, _currentHost == host && RailPresenter.Same(session.Path, _current), menu, Open, Language, RunFor(host, session.Path));
    }
}
