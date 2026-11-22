using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Pages.Rail;

/// <summary>
/// The left column: every project the kernel knows, and the conversations saved
/// under each.
/// </summary>
/// <remarks>
/// A conversation row carries what a reader scans for — an unread dot, a pin, the
/// running mark — and its menu carries what changes it. Copies and versions do
/// not get rows: they are reached from the row that owns them, so one
/// conversation cannot fill the column with files the person never made.
///
/// </remarks>
public sealed partial class ProjectRail : UserControl
{
    /// <summary>
    /// The column's rows.
    /// </summary>
    private readonly StackPanel _rows;

    /// <summary>Projects whose hidden session rows have been requested.</summary>
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);

    /// <summary>
    /// The projects the reader folded shut, by root.
    /// </summary>
    private readonly HashSet<string> _collapsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The tree as it stands.
    /// </summary>
    private IReadOnlyList<TreeWorkspace> _tree = Array.Empty<TreeWorkspace>();

    /// <summary>
    /// The transcript this window has open, or null.
    /// </summary>
    private string? _current;

    /// <summary>The selected projection of canonical session state.</summary>
    private string _scope = "all";

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectRail"/> class.
    /// </summary>
    public ProjectRail()
    {
        InitializeComponent();
        _rows = Rows;
        FoldAll.Click += (_, _) =>
        {
            var keys = _tree.Select(w => w.Root).Concat(_remotes.SelectMany(h => h.Projects.Select(w => RemoteKey(h.Name, w.Root)))).ToArray();
            var allFolded = keys.Length > 0 && keys.All(_collapsed.Contains);
            foreach (var key in keys)
            {
                if (allFolded)
                {
                    _collapsed.Remove(key);
                }
                else
                {
                    _collapsed.Add(key);
                }
            }

            Rebuild();
        };
        AddProject.Click += (_, _) => AddRequested?.Invoke();
        RecentTab.Click += (_, _) => ShowRecent();
        ProjectTab.Click += (_, _) =>
        {
            _scope = "all";
            Rebuild();
        };
        RefreshLabels();
    }

    /// <summary>
    /// Raised when the person asks to open a conversation.
    /// </summary>
    public event Action<string>? Opened;

    /// <summary>Raised when the person asks to choose another project folder.</summary>
    public event Action? AddRequested;

    /// <summary>
    /// Raised when the person pins or unpins a conversation.
    /// </summary>
    public event Action<string, bool>? Pinned;

    /// <summary>
    /// Raised when the person archives or unarchives a conversation.
    /// </summary>
    public event Action<string, bool>? Archived;

    /// <summary>
    /// Raised when the person says a conversation has been read.
    /// </summary>
    public event Action<string>? MarkedViewed;

    /// <summary>
    /// Gets or sets the language the rail's own words are drawn in.
    /// </summary>
    /// <remarks>
    /// Conversation titles are drawn as the kernel stored them; only the labels
    /// this control adds are translated.
    /// </remarks>
    public InterfaceLanguage Language
    {
        get; set;
    } = InterfaceLanguage.Zh;

    /// <summary>Updates the visible row labels using the selected language.</summary>
    public void RefreshLabels()
    {
        ProjectTab.Content = MessageCatalog.Get("common.project", Language);
        RecentTab.Content = MessageCatalog.Get("session.recent", Language);
        RecentTitle.Text = MessageCatalog.Get("session.recent_sessions", Language);
        AutomationProperties.SetName(AddProject, MessageCatalog.Get("session.open_project", Language));
        ToolTip.SetTip(AddProject, MessageCatalog.Get("session.open_project", Language));
        var menu = new MenuFlyout();
        foreach (var (scope, key) in new[] { ("all", "common.all"), ("unread", "common.unread"), ("pinned", "session.pinned_2"), ("archived", "common.archived") })
        {
            var item = new MenuItem { Header = MessageCatalog.Get(key, Language) };
            AutomationProperties.SetAutomationId(item, "action:session.scope." + scope);
            item.Click += (_, _) =>
            {
                _scope = scope;
                Rebuild();
            };
            menu.Items.Add(item);
        }

        Scope.Flyout = menu;
        Rebuild();
    }

    /// <summary>
    /// Draws one tree.
    /// </summary>
    /// <param name="tree">The projects the kernel listed, or null for none.</param>
    /// <param name="current">The transcript this window has open, or null.</param>
    public void Show(IReadOnlyList<TreeWorkspace>? tree, string? current)
    {
        _tree = tree ?? Array.Empty<TreeWorkspace>();
        _current = current;
        Rebuild();
    }

    /// <summary>
    /// Rebuilds every row from the tree as it stands.
    /// </summary>
    private void Rebuild()
    {
        _rows.Children.Clear();

        FoldAll.IsEnabled = _tree.Count > 0 || _remotes.Count > 0;
        RecentTab.IsEnabled = _tree.Any(w => w.Sessions.Count > 0) || _remotes.Any(h => h.Projects.Any(w => w.Sessions.Count > 0));
        var allFolded = _tree.Select(w => w.Root).Concat(_remotes.SelectMany(h => h.Projects.Select(w => RemoteKey(h.Name, w.Root)))).All(_collapsed.Contains);
        FoldShape.Data = Geometry.Parse(allFolded ? "M15 3h6v6 M21 3l-7 7 M3 15v6h6 M3 21l7-7" : "m14 10 7-7 M20 10h-6V4 m-11 17 7-7 M4 14h6v6");
        AutomationProperties.SetName(FoldAll, MessageCatalog.Get(allFolded ? "session.expand_all_projects" : "session.collapse_all_projects", Language));
        ToolTip.SetTip(FoldAll, AutomationProperties.GetName(FoldAll));
        ScopeLabel.Text = MessageCatalog.Get(
            _scope switch
            {
                "unread" => "common.unread",
                "pinned" => "session.pinned_2",
                "archived" => "common.archived",
                _ => "common.all",
            },
            Language);
        if (_tree.Count == 0 && _remotes.Count == 0)
        {
            _rows.Children.Add(EmptyState());
            return;
        }

        foreach (var workspace in _tree)
        {
            var sessions = RailPresenter.Sorted(workspace.Sessions).Where(MatchesScope).ToArray();
            if (_scope != "all" && sessions.Length == 0)
            {
                continue;
            }

            _rows.Children.Add(new ProjectEntry(workspace, _collapsed.Contains(workspace.Root), () =>
            {
                if (!_collapsed.Remove(workspace.Root))
                {
                    _collapsed.Add(workspace.Root);
                }

                Rebuild();
            }));
            if (_collapsed.Contains(workspace.Root))
            {
                continue;
            }

            var showAll = _expanded.Contains(workspace.Root) || sessions.Skip(5).Any(s => RailPresenter.Same(s.Path, _current));
            var visible = showAll ? sessions.Length : 5;
            foreach (var session in sessions.Take(visible))
            {
                _rows.Children.Add(SessionRow(session));
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
                    _expanded.Add(workspace.Root);
                    Rebuild();
                };
                _rows.Children.Add(more);
            }
        }

        RebuildRemotes();
    }

    /// <summary>Projects only facts explicitly carried by the canonical tree.</summary>
    /// <param name="session">Session to project.</param>
    /// <returns>Whether it belongs in the selected scope.</returns>
    private bool MatchesScope(TreeSession session) => _scope switch
    {
        "unread" => session.Unread && !session.Archived,
        "pinned" => session.Pinned && !session.Archived,
        "live" => session.RuntimeId.Length > 0 && !session.Archived,
        "archived" => session.Archived,
        _ => !session.Archived,
    };

    /// <summary>Fills the recent-session flyout from the current canonical tree.</summary>
    private void ShowRecent()
    {
        RecentRows.Children.Clear();
        var local = _tree.SelectMany(w => w.Sessions.Select(s => (Host: string.Empty, Root: w.Root, Session: s)));
        var remote = _remotes.SelectMany(h => h.Projects.SelectMany(w => w.Sessions.Select(s => (Host: h.Name, Root: w.Root, Session: s))));
        foreach (var row in local.Concat(remote).Where(row => !row.Session.Archived).OrderByDescending(row => row.Session.LastActiveAt))
        {
            RecentRows.Children.Add(row.Host.Length == 0 ? SessionRow(row.Session) : RemoteSessionRow(row.Host, row.Root, row.Session));
        }
    }

    /// <summary>
    /// Builds what the column says when the kernel lists no project at all.
    /// </summary>
    /// <returns>The empty state.</returns>
    /// <remarks>
    /// An empty column and a column that has not been read look the same, and the
    /// difference matters: one is a machine with nothing to show, the other is a
    /// read that has not answered yet. This says the first.
    /// </remarks>
    private Control EmptyState()
    {
        return new TextBlock
        {
            Text = MessageCatalog.Get(MessageKey.Session.AddAProject, Language),
            [!TextBlock.FontSizeProperty] = StudioTypography.Size(11),
            Opacity = 0.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(6, 8, 6, 0),
        };
    }

    /// <summary>Builds the session's native row and its authoritative action menu.</summary>
    /// <param name="session">Canonical conversation.</param>
    /// <returns>The row.</returns>
    private Control SessionRow(TreeSession session) => new SessionEntry(
        session,
        _currentHost.Length == 0 && RailPresenter.Same(session.Path, _current),
        SessionMenu(session),
        () =>
        {
            RecentTab.Flyout?.Hide();
            Opened?.Invoke(session.Path);
        },
        Language,
        RunFor(string.Empty, session.Path));

    /// <summary>
    /// Builds the menu one conversation offers.
    /// </summary>
    /// <param name="session">The conversation.</param>
    /// <returns>The menu, with only the actions that make sense for this row.</returns>
    private MenuFlyout SessionMenu(TreeSession session)
    {
        var menu = new MenuFlyout();

        var open = new MenuItem { Header = MessageCatalog.Get(MessageKey.Common.Open, Language) };
        open.Click += (_, _) => Opened?.Invoke(session.Path);
        menu.Items.Add(open);

        var pin = new MenuItem
        {
            Header = MessageCatalog.Get(
                session.Pinned ? MessageKey.Common.Unpin : MessageKey.Common.PinSession,
                Language),
        };
        pin.Click += (_, _) => Pinned?.Invoke(session.Path, !session.Pinned);
        menu.Items.Add(pin);

        var archive = new MenuItem
        {
            Header = MessageCatalog.Get(
                session.Archived ? MessageKey.Common.Unarchive : MessageKey.Common.ArchiveSession,
                Language),
        };
        archive.Click += (_, _) => Archived?.Invoke(session.Path, !session.Archived);
        menu.Items.Add(archive);

        if (session.Unread)
        {
            var seen = new MenuItem { Header = MessageCatalog.Get(MessageKey.Common.MarkAsRead, Language) };
            seen.Click += (_, _) => MarkedViewed?.Invoke(session.Path);
            menu.Items.Add(seen);
        }

        return menu;
    }
}
