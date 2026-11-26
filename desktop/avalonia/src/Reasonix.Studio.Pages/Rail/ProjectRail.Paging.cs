using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Pages.Rail;

public sealed partial class ProjectRail
{
    /// <summary>Canonical per-machine project bounds for the current view.</summary>
    private readonly Dictionary<string, Dictionary<string, int>> _wanted = new(StringComparer.Ordinal);

    /// <summary>Projects whose requested page is still being acknowledged.</summary>
    private readonly HashSet<string> _paging = new(StringComparer.Ordinal);

    /// <summary>The canonical search that owns the current page bounds.</summary>
    private string _query = string.Empty;

    /// <summary>The last canonical request preserves shared appearance-independent preferences.</summary>
    private TreeWindow _asked = new();

    /// <summary>The machine-local hover action preference.</summary>
    private bool _quickActions;

    /// <summary>The failed canonical window, retained for an explicit retry.</summary>
    private Func<Task>? _retryWindow;

    /// <summary>Raised to read a local or remote canonical sidebar window.</summary>
    public event Func<string, TreeWindow, Task>? WindowRequested;

    /// <summary>Gets the currently visible address-book machine names.</summary>
    public IReadOnlyList<string> RemoteNames => _remotes.Select(machine => machine.Name).ToArray();

    /// <summary>Gets or sets a value indicating whether row-level pin and archive actions are enabled.</summary>
    public bool QuickActions
    {
        get
        {
            return _quickActions;
        }

        set
        {
            if (_quickActions != value)
            {
                _quickActions = value;
                Rebuild();
            }
        }
    }

    /// <summary>Builds the same bounded canonical window for initial reads and refreshes.</summary>
    /// <param name="query">Search terms.</param><param name="hideLegacy">Whether to exclude legacy sessions.</param>
    /// <param name="host">Remote machine or empty for local.</param><returns>The current canonical window.</returns>
    public TreeWindow WindowFor(string query, bool hideLegacy, string host = "")
    {
        var next = query.Trim();
        if (_query != next)
        {
            _query = next;
            _wanted.Clear();
        }

        _asked = new TreeWindow
        {
            Limit = 50,
            Limits = _wanted.TryGetValue(host, out var limits) ? new Dictionary<string, int>(limits, StringComparer.Ordinal) : null,
            Scope = _scope == "archived" ? "archived" : _query.Length == 0 ? "active" : string.Empty,
            Query = _query,
            HideLegacy = hideLegacy,
        };
        return _asked;
    }

    /// <summary>Projects one acknowledged remote page while retaining saved projects not in it.</summary>
    /// <param name="host">Canonical machine name.</param><param name="tree">Acknowledged project window.</param>
    public void ShowRemotePage(string host, IReadOnlyList<TreeWorkspace> tree)
    {
        _remotes = _remotes.Select(machine => machine.Name != host
            ? machine
            : machine with
            {
                Projects = tree.Concat(machine.Projects.Where(project => !tree.Any(next => next.Root == project.Root))).ToArray(),
            }).ToArray();
        Rebuild();
    }

    /// <summary>Projects the actual turn lifetime into a scope decision.</summary>
    /// <param name="host">Canonical machine.</param><param name="session">Canonical row.</param>
    /// <returns>Whether the row belongs in the selected scope.</returns>
    private bool InScope(string host, TreeSession session) => _scope switch
    {
        "archived" => session.Archived,
        _ when session.Archived => _scope == "all" && _query.Length > 0,
        "unread" => session.Unread,
        "pinned" => session.Pinned,
        "live" => ListedAsRunning(host, session.Path),
        "waiting" => RunFor(host, session.Path) == "halt",
        _ => true,
    };

    /// <summary>Draws six canonical scopes, their loaded-row counts and an explicit clear action.</summary>
    private void UpdateScopeMenu()
    {
        var menu = new MenuFlyout();
        var rows = SearchEntries;
        foreach (var (scope, key) in new[]
        {
            ("all", "common.all"), ("live", "plan.step.now"), ("waiting", "chat.needs_you"),
            ("unread", "common.unread"), ("pinned", "session.pinned_2"), ("archived", "common.archived"),
        })
        {
            if (scope == "archived")
            {
                menu.Items.Add(new Separator());
            }

            var count = rows.Count(row => scope switch
            {
                "live" => !row.Session.Archived && ListedAsRunning(row.Target.Host, row.Session.Path),
                "waiting" => !row.Session.Archived && RunFor(row.Target.Host, row.Session.Path) == "halt",
                "unread" => !row.Session.Archived && row.Session.Unread,
                "pinned" => !row.Session.Archived && row.Session.Pinned,
                "archived" => row.Session.Archived,
                _ => !row.Session.Archived,
            });
            var label = MessageCatalog.Get(key, Language);
            var item = new MenuItem
            {
                Header = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    ColumnSpacing = 24,
                    Children = { new TextBlock { Text = label } },
                },
            };
            if (scope != "archived")
            {
                var badge = new TextBlock { Text = count.ToString(System.Globalization.CultureInfo.InvariantCulture), HorizontalAlignment = HorizontalAlignment.Right };
                Grid.SetColumn(badge, 1);
                ((Grid)item.Header).Children.Add(badge);
            }

            AutomationProperties.SetAutomationId(item, "action:session.scope." + scope);
            AutomationProperties.SetName(item, scope == "archived" ? label : label + " " + count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            item.Click += async (_, _) => await SelectScopeAsync(scope).ConfigureAwait(true);
            menu.Items.Add(item);
        }

        Scope.Flyout = menu;
        ClearScope.IsVisible = _scope != "all";
        Scope.Margin = _scope == "all" ? default : new Thickness(0, 0, 22, 0);
        Toolbar.ColumnDefinitions[4].Width = new GridLength(_scope == "all" ? 54 : 76);
        ClearScope.Click -= OnClearScope;
        ClearScope.Click += OnClearScope;
        AutomationProperties.SetName(ClearScope, MessageCatalog.Get("session.clear_filter", Language));
        RetrySidebar.Content = MessageCatalog.Get("common.retry", Language);
    }

    /// <summary>Keeps the current conversation outside the preview without expanding its unrelated rows.</summary>
    /// <param name="host">Owning machine.</param><param name="key">Scoped project disclosure.</param>
    /// <param name="sessions">Matching canonical rows.</param><returns>The visible preview population.</returns>
    private TreeSession[] PreviewRows(string host, string key, TreeSession[] sessions)
    {
        var searched = host.Length == 0 ? _query.Length > 0 : _remoteQuery.Length > 0;
        return _scope != "all" || searched || _expanded.Contains(key) || (host.Length == 0 && _selection is not null)
            ? sessions
            : sessions.Where((session, index) => index < 5 || (host == _currentHost && RailPresenter.Same(session.Path, _current))).ToArray();
    }

    /// <summary>Clears a scope using the same canonical read as choosing one.</summary>
    /// <param name="sender">The native clear action.</param><param name="e">The routed click.</param>
    private async void OnClearScope(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => await SelectScopeAsync("all").ConfigureAwait(true);

    /// <summary>Resets the page bounds when the requested canonical view changes.</summary>
    /// <param name="scope">Requested session scope.</param><param name="retry">Whether to retry the same canonical scope.</param><returns>The canonical acknowledgement.</returns>
    private async Task SelectScopeAsync(string scope, bool retry = false)
    {
        var before = _scope == "archived";
        _scope = scope;
        var changed = before != (_scope == "archived");
        if (changed)
        {
            _wanted.Clear();
        }

        Rebuild();
        if ((changed || retry) && WindowRequested is not null)
        {
            try
            {
                ReadFailure.IsVisible = RetrySidebar.IsVisible = false;
                await WindowRequested.Invoke(string.Empty, WindowFor(_query, _asked.HideLegacy)).ConfigureAwait(true);
            }
            catch (Exception error)
            {
                ReadFailure.Text = error.Message;
                ReadFailure.IsVisible = RetrySidebar.IsVisible = true;
                _retryWindow = () => SelectScopeAsync(scope, true);
            }
        }
    }

    /// <summary>Draws the next-page action for canonical remaining rows as well as loaded previews.</summary>
    /// <param name="host">Machine identity.</param><param name="workspace">Canonical project.</param>
    /// <param name="loaded">Loaded matching rows.</param><param name="visible">Rows currently shown.</param>
    /// <returns>The native page action.</returns>
    private Button MoreButton(string host, TreeWorkspace workspace, int loaded, int visible)
    {
        var key = host.Length == 0 ? workspace.Root : RemoteKey(host, workspace.Root);
        var remaining = Math.Max(0, loaded - visible) + workspace.Remaining;
        var more = new Button
        {
            Content = MessageCatalog.Get(MessageKey.Common.ShowMoreNLeft, Language, ("n", Format.Count(remaining, Language))),
            IsEnabled = !_paging.Contains(key),
            Classes = { "rail-button", "session-more" },
        };
        AutomationProperties.SetAutomationId(more, "action:session.more");
        more.Click += async (_, _) =>
        {
            _expanded.Add(key);
            if (workspace.Remaining == 0 || workspace.Sessions.Count >= TreeWindow.MaxRows)
            {
                Rebuild();
                return;
            }

            await ReadMoreAsync(host, workspace, key).ConfigureAwait(true);
        };
        return more;
    }

    /// <summary>Preserves loaded previews at the canonical ceiling and names search for older rows.</summary>
    /// <param name="rows">The target row collection.</param>
    /// <param name="host">Owning machine.</param><param name="workspace">Canonical project.</param>
    /// <param name="loaded">Matching loaded rows.</param><param name="visible">Currently displayed rows.</param>
    private void AddWindowTail(ICollection<Control> rows, string host, TreeWorkspace workspace, int loaded, int visible)
    {
        if (_scope is not ("all" or "archived") || (loaded <= visible && workspace.Remaining == 0))
        {
            return;
        }

        if (loaded > visible || workspace.Sessions.Count < TreeWindow.MaxRows)
        {
            rows.Add(MoreButton(host, workspace, loaded, visible));
            return;
        }

        rows.Add(new TextBlock
        {
            Classes = { "session-window-note" },
            Text = MessageCatalog.Get(workspace.CountsComplete == false ? "session.search_to_find_more_conversations" : "session.n_older_conversations_use_search", Language, ("n", Format.Count(workspace.Remaining, Language))),
        });
    }

    /// <summary>Requests and acknowledges one larger window, rolling its bound back on refusal.</summary>
    /// <param name="host">Owning machine.</param><param name="workspace">Canonical project.</param><param name="key">Scoped disclosure identity.</param>
    /// <returns>The completed window read.</returns>
    private async Task ReadMoreAsync(string host, TreeWorkspace workspace, string key)
    {
        var before = _wanted.TryGetValue(host, out var existing) ? new Dictionary<string, int>(existing, StringComparer.Ordinal) : new Dictionary<string, int>(StringComparer.Ordinal);
        var next = new Dictionary<string, int>(before, StringComparer.Ordinal) { [workspace.Root] = Math.Min(TreeWindow.MaxRows, Math.Max(50, before.GetValueOrDefault(workspace.Root, 50)) + 50) };
        _wanted[host] = next;
        _paging.Add(key);
        ReadFailure.IsVisible = RetrySidebar.IsVisible = false;
        Rebuild();
        try
        {
            if (WindowRequested is not null)
            {
                await WindowRequested.Invoke(host, WindowFor(_query, _asked.HideLegacy, host)).ConfigureAwait(true);
            }
        }
        catch (Exception error)
        {
            _wanted[host] = before;
            ReadFailure.Text = error.Message;
            ReadFailure.IsVisible = RetrySidebar.IsVisible = true;
            _retryWindow = () => ReadMoreAsync(host, workspace, key);
        }
        finally
        {
            _paging.Remove(key);
            Rebuild();
        }
    }
}
