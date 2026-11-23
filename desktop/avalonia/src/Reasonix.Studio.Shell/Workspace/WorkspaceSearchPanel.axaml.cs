using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Shell.Workspace;

/// <summary>The keyboard-accessible search and commands overlay.</summary>
public sealed partial class WorkspaceSearchPanel : UserControl
{
    /// <summary>The scope requested by its tab.</summary>
    private string _scope = string.Empty;

    /// <summary>The language used by section labels.</summary>
    private InterfaceLanguage _language;

    /// <summary>The currently available canonical actions.</summary>
    private IReadOnlyList<WorkspaceSearchHit> _entries = Array.Empty<WorkspaceSearchHit>();

    /// <summary>Initializes a new instance of the <see cref="WorkspaceSearchPanel"/> class.</summary>
    public WorkspaceSearchPanel()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            Sheet.Margin = new Avalonia.Thickness(16, Bounds.Height * 0.12, 16, 24);
            Sheet.MaxHeight = Math.Max(200, Bounds.Height * 0.68);
        };
        Query.TextChanged += (_, _) => Filter();
        Close.Click += (_, _) => Hide();
        foreach (var (button, scope) in new[] { (All, string.Empty), (Actions, "actions"), (Sessions, "sessions"), (Settings, "settings") })
        {
            button.Click += (_, _) =>
            {
                _scope = _scope == scope ? string.Empty : scope;
                Filter();
                Query.Focus();
            };
        }

        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Results.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left && e.Source is Avalonia.Visual visual
                && (visual is ListBoxItem || visual.GetVisualAncestors().OfType<ListBoxItem>().Any()))
            {
                Pick();
            }
        };
        PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.Source, this))
            {
                Hide();
            }
        };
    }

    /// <summary>Raised when the overlay gives focus back to its opener.</summary>
    public event Action? Closed;

    /// <summary>Raised when a canonical action joins the three most recently used entries.</summary>
    public event Action<IReadOnlyList<string>>? RecentChanged;

    /// <summary>Gets or sets the machine-local canonical recently used entries.</summary>
    public IReadOnlyList<string> Recent { get; set; } = Array.Empty<string>();

    /// <summary>Shows a fresh search with current canonical entries.</summary>
    /// <param name="entries">Available operations and sessions.</param><param name="language">Interface language.</param>
    public void Open(IReadOnlyList<WorkspaceSearchHit> entries, InterfaceLanguage language)
    {
        _entries = entries;
        _language = language;
        _scope = string.Empty;
        Query.Text = string.Empty;
        Query.PlaceholderText = MessageCatalog.Get("app.search_conversations_tools_settings", language);
        All.Content = MessageCatalog.Get("common.all", language);
        Actions.Content = "> " + MessageCatalog.Get("app.action", language);
        Sessions.Content = "# " + MessageCatalog.Get("common.session", language);
        Settings.Content = ", " + MessageCatalog.Get("common.settings", language);
        Hint.Text = language == InterfaceLanguage.Zh ? "↑ ↓ 选择 · Enter 打开 · Tab 切换范围" : "↑ ↓ Select · Enter Open · Tab Switch scope";
        ScopeHint.Text = Actions.Content + " · " + Sessions.Content + " · " + Settings.Content;
        IsVisible = true;
        Filter();
        Dispatcher.UIThread.Post(() => Query.Focus());
    }

    /// <summary>Closes the search and restores the opener.</summary>
    public void Hide()
    {
        IsVisible = false;
        Closed?.Invoke();
    }

    /// <summary>Projects query prefixes and explicit scopes through one filter.</summary>
    private void Filter()
    {
        var q = Query.Text?.Trim() ?? string.Empty;
        if (_scope.Length == 0 && q.Length > 0 && q[0] is '>' or '#' or ',')
        {
            _scope = q[0] switch
            {
                '>' => "actions",
                '#' => "sessions",
                _ => "settings",
            };
            Query.Text = q[1..].Trim();
            Filter();
            return;
        }

        ScopeBadge.IsVisible = _scope.Length > 0;
        ScopeBadgeLabel.Text = _scope.Length > 0 ? MessageCatalog.Get(_scope == "actions" ? "app.action" : _scope == "sessions" ? "common.session" : "common.settings", _language) : string.Empty;
        Actions.Classes.Set("active", _scope == "actions");
        Sessions.Classes.Set("active", _scope == "sessions");
        Settings.Classes.Set("active", _scope == "settings");
        Results.ItemsSource = OrderedHits(q);
        Results.SelectedIndex = Results.ItemCount > 1 ? 1 : -1;
        Empty.Text = MessageCatalog.Get("app.nothing_matches", _language);
        Empty.IsVisible = Results.ItemCount == 0;
    }

    /// <summary>Dispatches the highlighted canonical operation after closing.</summary>
    private void Pick()
    {
        if (Results.SelectedItem is WorkspaceSearchHit { Selectable: true } hit)
        {
            Recent = new[] { hit.CanonicalKey }.Concat(Recent.Where(key => key != hit.CanonicalKey)).Take(3).ToArray();
            RecentChanged?.Invoke(Recent);
            Hide();
            hit.Run();
        }
    }

    /// <summary>Keeps keyboard selection inside the palette and handles close.</summary>
    /// <param name="sender">Event source.</param><param name="e">Pressed key.</param>
    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
        }
        else if (e.Key == Key.Enter)
        {
            Pick();
        }
        else if (e.Key is Key.Down or Key.Up)
        {
            var direction = e.Key == Key.Down ? 1 : -1;
            var at = Results.SelectedIndex;
            for (var count = 0; count < Results.ItemCount; count++)
            {
                at = (at + direction + Results.ItemCount) % Results.ItemCount;
                if (Results.Items[at] is WorkspaceSearchHit { Selectable: true })
                {
                    Results.SelectedIndex = at;
                    break;
                }
            }

            if (Results.SelectedItem is { } selected)
            {
                Results.ScrollIntoView(selected);
            }
        }
        else if (e.Key == Key.Tab)
        {
            var scopes = new[] { string.Empty, "actions", "sessions", "settings" };
            _scope = scopes[(Array.IndexOf(scopes, _scope) + 1) % scopes.Length];
            Filter();
            Query.Focus();
        }
        else if (e.Key == Key.Back && string.IsNullOrEmpty(Query.Text) && _scope.Length > 0)
        {
            _scope = string.Empty;
            Filter();
        }
        else
        {
            return;
        }

        e.Handled = true;
    }
}
