using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Workspace;

/// <summary>Canonical recent history with machine selection, search, paging and retry.</summary>
public sealed partial class RecentSessionsPanel : UserControl
{
    /// <summary>Search changes use the reference's quarter-second coalescing window.</summary>
    private readonly DispatcherTimer _search = new() { Interval = TimeSpan.FromMilliseconds(250) };

    /// <summary>Reads the selected machine's authoritative history window.</summary>
    private Func<string, TreeWindow, CancellationToken, Task<IReadOnlyList<TreeWorkspace>>>? _read;

    /// <summary>Navigation completes only after the target has been acknowledged.</summary>
    private Func<SessionOpenTarget, Task>? _navigate;

    /// <summary>Reads reducer-owned runtime activity without inferring it from an open descriptor.</summary>
    private Func<SessionOpenTarget, string>? _run;

    /// <summary>The current read owner, cancelled when the dialog changes or closes.</summary>
    private CancellationTokenSource? _reading;

    /// <summary>The language of the native dialog.</summary>
    private InterfaceLanguage _language;

    /// <summary>The current canonical recent offset.</summary>
    private int _offset;

    /// <summary>Suppresses writes while projecting the dialog's initial choices.</summary>
    private bool _showing;

    /// <summary>Preserves the existing hide-legacy preference in every query.</summary>
    private bool _hideLegacy;

    /// <summary>Initializes a new instance of the <see cref="RecentSessionsPanel"/> class.</summary>
    public RecentSessionsPanel()
    {
        InitializeComponent();
        Query.TextChanged += (_, _) =>
        {
            if (!_showing)
            {
                _offset = 0;
                _search.Stop();
                _search.Start();
            }
        };
        _search.Tick += async (_, _) =>
        {
            _search.Stop();
            await ReloadAsync().ConfigureAwait(true);
        };
        Machine.SelectionChanged += async (_, _) =>
        {
            if (!_showing)
            {
                _offset = 0;
                await ReloadAsync().ConfigureAwait(true);
            }
        };
        Previous.Click += async (_, _) =>
        {
            _offset = Math.Max(0, _offset - 50);
            await ReloadAsync().ConfigureAwait(true);
        };
        Next.Click += async (_, _) =>
        {
            _offset += 50;
            await ReloadAsync().ConfigureAwait(true);
        };
        Retry.Click += async (_, _) => await ReloadAsync().ConfigureAwait(true);
        Close.Click += (_, _) => Hide();
        Rows.PointerReleased += async (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left && e.Source is Avalonia.Visual visual
                && (visual is ListBoxItem || visual.GetVisualAncestors().OfType<ListBoxItem>().Any()))
            {
                await PickAsync().ConfigureAwait(true);
            }
        };
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
    }

    /// <summary>Raised when the dialog restores its opener.</summary>
    public event Action? Closed;

    /// <summary>Opens a fresh canonical recent query for local or remote history.</summary>
    /// <param name="hosts">Address-book machine names.</param><param name="read">Canonical reader.</param>
    /// <param name="navigate">Acknowledged navigation.</param><param name="language">Interface language.</param>
    /// <param name="hideLegacy">Existing legacy-session preference.</param><param name="run">Reducer-owned activity reader.</param><returns>The first page read.</returns>
    public async Task OpenAsync(IReadOnlyList<string> hosts, Func<string, TreeWindow, CancellationToken, Task<IReadOnlyList<TreeWorkspace>>> read, Func<SessionOpenTarget, Task> navigate, InterfaceLanguage language, bool hideLegacy, Func<SessionOpenTarget, string>? run = null)
    {
        _showing = true;
        _read = read;
        _navigate = navigate;
        _run = run;
        _language = language;
        _hideLegacy = hideLegacy;
        _offset = 0;
        Query.Text = string.Empty;
        Machine.ItemsSource = new[] { MessageCatalog.Get("session.this_machine", language) }.Concat(hosts).ToArray();
        Machine.SelectedIndex = 0;
        Machine.IsVisible = hosts.Count > 0;
        Title.Text = MessageCatalog.Get("session.recent_sessions", language);
        Description.Text = MessageCatalog.Get("session.ordered_by_last_activity_including", language);
        KeyboardHint.Text = MessageCatalog.Get("session.to_choose_enter_to_open", language);
        Query.PlaceholderText = MessageCatalog.Get("session.search_recent_sessions_or_projects", language);
        Previous.Content = MessageCatalog.Get("common.previous_page", language);
        Next.Content = MessageCatalog.Get("common.next_page", language);
        Retry.Content = MessageCatalog.Get("common.retry", language);
        Close.Content = MessageCatalog.Get("common.close", language);
        _showing = false;
        IsVisible = true;
        await ReloadAsync().ConfigureAwait(true);
        Query.Focus();
    }

    /// <summary>Reads the latest requested window and rejects late or incompatible pages.</summary>
    /// <returns>The acknowledged recent page.</returns>
    public async Task ReloadAsync()
    {
        if (_read is null)
        {
            return;
        }

        _search.Stop();
        _reading?.Cancel();
        var owner = new CancellationTokenSource();
        _reading = owner;
        var host = Machine.SelectedIndex > 0 ? Machine.SelectedItem?.ToString() ?? string.Empty : string.Empty;
        var query = new TreeWindow { Order = "recent", Scope = "active", Limit = 50, Offset = _offset, Query = Query.Text?.Trim() ?? string.Empty, HideLegacy = _hideLegacy };
        Status.Text = MessageCatalog.Get("session.reading_sessions", _language);
        Status.IsVisible = true;
        Retry.IsVisible = false;
        Previous.IsEnabled = Next.IsEnabled = false;
        try
        {
            var tree = await _read(host, query, owner.Token).ConfigureAwait(true);
            if (!ReferenceEquals(owner, _reading) || !IsVisible)
            {
                return;
            }

            if (tree.Any(workspace => workspace.RecentOffset != _offset))
            {
                throw new InvalidOperationException(MessageCatalog.Get("session.this_environment_does_not_support", _language));
            }

            var rows = RecentRows(tree, host);
            Rows.ItemsSource = rows;
            RefreshRuns();
            Rows.SelectedIndex = rows.Length > 0 ? 0 : -1;
            Status.Text = MessageCatalog.Get("common.nothing_matches", _language);
            Status.IsVisible = rows.Length == 0;
            Previous.IsEnabled = _offset > 0;
            Next.IsEnabled = tree.Sum(workspace => workspace.Remaining) > 0;
            Page.Text = MessageCatalog.Get("session.page_n", _language, ("n", ((_offset / 50) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (ReferenceEquals(owner, _reading))
            {
                Status.Text = error.Message;
                Status.IsVisible = Retry.IsVisible = true;
            }
        }
        finally
        {
            if (ReferenceEquals(owner, _reading))
            {
                _reading = null;
            }

            owner.Dispose();
        }
    }

    /// <summary>Closes the dialog without accepting an unacknowledged result.</summary>
    public void Hide()
    {
        _search.Stop();
        _reading?.Cancel();
        _reading = null;
        IsVisible = false;
        Closed?.Invoke();
    }

    /// <summary>Opens the selected canonical target and retains a refusal in the dialog.</summary>
    /// <returns>The acknowledged navigation.</returns>
    private async Task PickAsync()
    {
        if (Rows.SelectedItem is not RecentSessionHit hit || _navigate is null)
        {
            return;
        }

        try
        {
            IsEnabled = false;
            await _navigate(hit.Target).ConfigureAwait(true);
            Hide();
        }
        catch (Exception error)
        {
            Status.Text = error.Message;
            Status.IsVisible = true;
        }
        finally
        {
            IsEnabled = true;
        }
    }

    /// <summary>Supports direct keyboard selection and dismissal.</summary>
    /// <param name="sender">Native source.</param><param name="e">Pressed key.</param>
    private async void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
        }
        else if (e.Key == Key.Enter && Rows.IsKeyboardFocusWithin)
        {
            await PickAsync().ConfigureAwait(true);
        }
        else if (e.Key == Key.Down && Query.IsKeyboardFocusWithin && Rows.ItemCount > 0)
        {
            Rows.SelectedIndex = 0;
            Rows.ContainerFromIndex(0)?.Focus();
        }
        else if (e.Key == Key.Up && Rows.IsKeyboardFocusWithin && Rows.SelectedIndex == 0)
        {
            Query.Focus();
        }
        else
        {
            return;
        }

        e.Handled = true;
    }
}
