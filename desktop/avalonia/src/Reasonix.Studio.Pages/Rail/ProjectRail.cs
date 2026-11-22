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
        InitializeBatch();
        InitializeGestures();
        InitializePointerStability();
        RetrySidebar.Click += async (_, _) =>
        {
            if (_retryWindow is not null)
            {
                await _retryWindow.Invoke().ConfigureAwait(true);
            }
        };
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
        RecentTab.Click += (_, _) => RecentRequested?.Invoke();
        ProjectTab.Click += async (_, _) => await SelectScopeAsync("all").ConfigureAwait(true);
        RefreshLabels();
    }

    /// <summary>
    /// Raised when the person asks to open a conversation.
    /// </summary>
    public event Action<string>? Opened;

    /// <summary>Raised when the person asks to choose another project folder.</summary>
    public event Action? AddRequested;

    /// <summary>Raised to read the canonical recent-session dialog.</summary>
    public event Action? RecentRequested;

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
        AutomationProperties.SetName(AddProject, MessageCatalog.Get("session.open_project", Language));
        ToolTip.SetTip(AddProject, MessageCatalog.Get("session.open_project", Language));
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
        if (DeferPointerRebuild())
        {
            return;
        }

        var editing = _rows.Children.OfType<SessionEntry>().FirstOrDefault(row => row.IsRenaming);
        var editPath = editing?.SessionPath;
        var editText = editing?.SuspendRename();
        _rows.Children.Clear();
        _displayed.Clear();
        UpdateScopeMenu();

        FoldAll.IsEnabled = _tree.Count > 0 || _remotes.Count > 0;
        RecentTab.IsEnabled = true;
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
                "live" => "plan.step.now",
                "waiting" => "chat.needs_you",
                _ => "common.all",
            },
            Language);
        if (_tree.Count == 0 && _remotes.Count == 0)
        {
            _rows.Children.Add(EmptyState());
            UpdateBatch();
            return;
        }

        foreach (var workspace in _tree)
        {
            var sessions = RailPresenter.Sorted(workspace.Sessions).Where(session => InScope(string.Empty, session)).ToArray();
            if (_scope != "all" && sessions.Length == 0)
            {
                continue;
            }

            var project = new ProjectEntry(workspace, _collapsed.Contains(workspace.Root), () =>
            {
                if (!_collapsed.Remove(workspace.Root))
                {
                    _collapsed.Add(workspace.Root);
                }

                Rebuild();
            });
            project.SetMenu(BuildProjectMenu(new SessionOpenTarget(string.Empty, workspace.Root, string.Empty)));
            ConfigureProjectDrag(project, workspace);
            project.SetNewAction(() => Navigate(new SessionOpenTarget(string.Empty, workspace.Root, string.Empty)), !workspace.Missing, Language);
            _rows.Children.Add(project);
            if (_collapsed.Contains(workspace.Root))
            {
                continue;
            }

            var visible = PreviewRows(string.Empty, workspace.Root, sessions);
            foreach (var session in visible)
            {
                _rows.Children.Add(SessionRow(session));
                AddKeptRows(workspace.Root, session);
            }

            AddWindowTail(string.Empty, workspace, sessions.Length, visible.Length);
        }

        RebuildRemotes();
        UpdateBatch();
        if (editPath is not null && editText is not null)
        {
            _rows.Children.OfType<SessionEntry>().FirstOrDefault(row => row.SessionPath == editPath)?.RestoreRename(editText);
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
    private Control SessionRow(TreeSession session)
    {
        _displayed.Add(session);
        var target = new SessionOpenTarget(string.Empty, session.WorkspaceRoot.Length > 0 ? session.WorkspaceRoot : _tree.FirstOrDefault(w => w.Sessions.Contains(session))?.Root ?? string.Empty, session.Path);
        return new SessionEntry(
        session,
        _currentHost.Length == 0 && RailPresenter.Same(session.Path, _current),
        BuildSessionMenu(target, session),
        () =>
        {
            if (_selection is null)
            {
                Navigate(target);
            }
        },
        Language,
        RunFor(string.Empty, session.Path),
        QuickActions,
        _selection?.Contains(session.Path),
        () => ToggleSelected(session.Path),
        title => RequestAction(new WorkspaceAction("session.rename", target, session, title)));
    }
}
