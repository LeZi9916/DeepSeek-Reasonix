using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.Shell.Session;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Owns all runtimes retained by this window.</summary>
    private WindowSessionCoordinator? _navigation;

    /// <summary>The pane currently mounted in the conversation surface.</summary>
    private SessionPaneState? _activePane;

    /// <summary>Orders all session navigation through one canonical target.</summary>
    /// <param name="target">Host, workspace and transcript identity.</param><returns>The latest visible navigation.</returns>
    internal Task OpenTargetAsync(SessionOpenTarget target) => NavigateAsync(target, null);

    /// <summary>Orders retained-tab activation through the same navigation ticket as every other entry.</summary>
    /// <param name="pane">The explicitly selected retained pane.</param><returns>The acknowledged navigation.</returns>
    private Task NavigatePaneAsync(SessionPaneState pane) => NavigateAsync(new SessionOpenTarget(pane.Host, pane.Status.WorkspaceRoot, pane.Status.SessionPath), pane);

    /// <summary>Preserves the current page until the latest requested history is readable.</summary>
    /// <param name="target">Canonical target.</param><param name="retained">Optional already open pane.</param><returns>The navigation result.</returns>
    private async Task NavigateAsync(SessionOpenTarget target, SessionPaneState? retained)
    {
        var identity = System.Text.Json.JsonSerializer.Serialize(target);
        var ticket = _switch.Begin(identity);
        NavigationProgress.IsVisible = true;
        try
        {
            await OpenTargetCoreAsync(target, false, () => _switch.IsCurrent(ticket), retained).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (_switch.IsCurrent(ticket))
            {
                if (error is Contracts.Refusal.IRefusal refusal && Pages.Session.SessionBoundaries.Of(refusal) == Pages.Session.SessionBoundary.TooLarge)
                {
                    OfferSessionTail(target);
                }
                else
                {
                    ShowRefusal(error);
                }
            }
        }
        finally
        {
            if (_switch.IsCurrent(ticket))
            {
                NavigationProgress.IsVisible = false;
            }

            _switch.End(ticket);
        }
    }

    /// <summary>Creates the shared coordinator and observes explicit idle folding.</summary>
    /// <param name="owner">The original host connection.</param>
    private void EnsureNavigation(SessionEngine owner)
    {
        if (_navigation is not null)
        {
            return;
        }

        _navigation = new WindowSessionCoordinator(owner) { Limit = _workspacePreferences.OpenSessionLimit };
        _navigation.Folded += title => ShowNote(MessageCatalog.Get("session.the_open_session_limit_n", Language, ("n", _navigation.Limit.ToString(System.Globalization.CultureInfo.CurrentCulture)), ("title", title)));
    }

    /// <summary>Connects pane and history actions to the shared coordinator.</summary>
    private void InitializeNavigation()
    {
        Activated += async (_, _) =>
        {
            if (_activePane is { } pane && ConversationArea.IsEffectivelyVisible)
            {
                await MarkPaneViewedAsync(pane).ConfigureAwait(true);
            }
        };
        EarlierHistory.Click += async (_, _) => await ReadEarlierHistoryAsync().ConfigureAwait(true);
        Projects.NavigateRequested += async target => await OpenTargetAsync(target).ConfigureAwait(true);
        PaneTabs.FocusRequested += async pane => await NavigatePaneAsync(pane).ConfigureAwait(true);
        PaneTabs.CloseRequested += async panes => await ClosePanesAsync(panes).ConfigureAwait(true);
        PaneTabs.RenameRequested += pane => Projects.RequestAction(new WorkspaceAction("session.rename-start", new SessionOpenTarget(pane.Host, pane.Status.WorkspaceRoot, pane.Status.SessionPath), new Contracts.Tree.TreeSession { Path = pane.Status.SessionPath, Title = pane.Status.Label }));
    }

    /// <summary>Retains an acknowledged runtime and subscribes to independent updates.</summary>
    /// <param name="status">Acknowledged runtime status.</param>
    private void CaptureNavigation(SessionStatus status)
    {
        if (_engine is not { } engine)
        {
            return;
        }

        EnsureNavigation(_launchOwner ?? engine);
        _activePane = _navigation!.Capture(engine, status);
        _navigation.Focused = _activePane;
        engine.HistoryChanged -= OnHistoryChanged;
        engine.HistoryChanged += OnHistoryChanged;
        engine.FrameReceived -= OnPaneFrame;
        engine.FrameReceived += OnPaneFrame;
        engine.HistoryInvalidated -= OnPaneGap;
        engine.HistoryInvalidated += OnPaneGap;
        DrawPaneTabs();
    }

    /// <summary>Updates tab titles and activity without replacing runtime state.</summary>
    private void DrawPaneTabs() => PaneTabs.Show(_navigation?.Panes ?? [], _activePane, Language);

    /// <summary>Locates a runtime and focuses it only for the current navigation.</summary>
    /// <param name="target">Canonical target.</param><param name="register">Whether to register the project.</param>
    /// <param name="current">Checks the navigation ticket.</param><param name="retained">Optional already open pane.</param><returns>The acknowledged navigation.</returns>
    private async Task OpenTargetCoreAsync(SessionOpenTarget target, bool register, Func<bool>? current = null, SessionPaneState? retained = null)
    {
        if (_engine is not { } previous)
        {
            return;
        }

        EnsureNavigation(_launchOwner ?? previous);
        if (_activePane is null)
        {
            _activePane = _navigation!.Capture(previous, _composerStatus);
        }

        _navigation!.Focused = _activePane;
        var pane = retained ?? await _navigation.OpenAsync(target, register, NewTaskPreset, _closing.Token).ConfigureAwait(true);
        var title = Projects.SearchEntries.FirstOrDefault(entry => entry.Target.Host == target.Host && RailPresenter.Same(entry.Target.SessionPath, target.SessionPath)).Session?.Title;
        if (!string.IsNullOrWhiteSpace(title))
        {
            pane.Status = pane.Status with
            {
                Label = title,
            };
        }

        DrawPaneTabs();
        EnsurePaneWatching(pane);
        if (!pane.Engine.History.Initialized)
        {
            await pane.Engine.LoadHistoryAsync(_closing.Token).ConfigureAwait(true);
        }

        if (_closing.IsCancellationRequested || (current is not null && !current()))
        {
            return;
        }

        await FocusPaneAsync(pane).ConfigureAwait(true);
    }

    /// <summary>Mounts a retained pane and independently reads its history and status.</summary>
    /// <param name="pane">Pane to focus.</param><returns>The mounted session.</returns>
    private async Task FocusPaneAsync(SessionPaneState pane)
    {
        if (_engine is null || (ReferenceEquals(_activePane, pane) && _sessionPath == pane.Status.SessionPath))
        {
            Composer.Focus();
            return;
        }

        SaveDraft();
        if (_activePane is { } previous)
        {
            previous.ScrollOffset = Conversation.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.Offset.Y ?? 0;
            previous.WorkbenchOpen = WorkbenchPanel.IsVisible;
            previous.FilePath = _openFile?.Original.Path;
        }

        _launchOwner ??= _engine;
        _engine = pane.Engine;
        _activePane = pane;
        pane.Unread = false;
        _sidebarHost = pane.Host;
        AdoptSessionPath(pane.Status.SessionPath);
        AdoptComposer(pane.Status);
        CrumbTitle.Text = pane.Status.Label.Length > 0 ? pane.Status.Label : MessageCatalog.Get("common.new_session", Language);
        ShowState(pane.Engine.State);
        ReturnToConversation();
        DrawPaneTabs();
        _ = MarkPaneViewedAsync(pane);
        EnsurePaneWatching(pane);

        await _navigation!.AcknowledgeReadAsync(pane, _closing.Token).ConfigureAwait(true);
        if (!ReferenceEquals(_activePane, pane))
        {
            return;
        }

        var history = ReadPaneHistoryAsync(pane);
        var status = ReadPaneStatusAsync(pane);
        await Task.WhenAll(history, status, LoadTreeAsync(), LoadQueueAsync()).ConfigureAwait(true);
        if (ReferenceEquals(_activePane, pane))
        {
            var scroll = Conversation.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (scroll is not null)
            {
                scroll.Offset = new Vector(0, pane.ScrollOffset);
            }

            if (pane.WorkbenchOpen)
            {
                await OpenWorkbenchAsync().ConfigureAwait(true);
                if (pane.FilePath is { Length: > 0 } file)
                {
                    await OpenEntryAsync(new Workbench.WorkspaceFileEntry(file, false)).ConfigureAwait(true);
                }
            }
        }
    }

    /// <summary>Starts each runtime subscription once, before its history can race with live arrivals.</summary>
    /// <param name="pane">The retained pane.</param>
    private void EnsurePaneWatching(SessionPaneState pane)
    {
        if (pane.Watching is not null)
        {
            return;
        }

        pane.Engine.Changed -= OnChanged;
        pane.Engine.Changed += OnChanged;
        pane.Engine.QueueChanged -= OnQueueChanged;
        pane.Engine.QueueChanged += OnQueueChanged;
        pane.Engine.TreeChanged -= OnTreeChanged;
        pane.Engine.TreeChanged += OnTreeChanged;
        pane.Engine.HistoryChanged -= OnHistoryChanged;
        pane.Engine.HistoryChanged += OnHistoryChanged;
        pane.Engine.FrameReceived -= OnPaneFrame;
        pane.Engine.FrameReceived += OnPaneFrame;
        pane.Engine.HistoryInvalidated -= OnPaneGap;
        pane.Engine.HistoryInvalidated += OnPaneGap;
        pane.Watching = WatchPaneAsync(pane);
    }

    /// <summary>Clears unread state through the owning host after the reader views a pane.</summary>
    /// <param name="pane">The pane being read.</param><returns>The metadata acknowledgement.</returns>
    private async Task MarkPaneViewedAsync(SessionPaneState pane)
    {
        if (pane.Status.SessionPath.Length == 0)
        {
            return;
        }

        try
        {
            if (pane.Host.Length == 0)
            {
                await pane.Engine.MarkSessionViewedAsync(pane.Status.SessionPath, _closing.Token).ConfigureAwait(true);
            }
            else
            {
                await pane.Engine.Pages.WriteRemoteSessionAsync(pane.Host, pane.Status.SessionPath, "viewed", false, _closing.Token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (ReferenceEquals(pane, _activePane))
            {
                ShowRefusal(error);
            }
        }
    }

    /// <summary>Keeps a pane subscribed while it is in the background.</summary>
    /// <param name="pane">Owning pane.</param><returns>The runtime stream lifetime.</returns>
    private async Task WatchPaneAsync(SessionPaneState pane)
    {
        try
        {
            await pane.Engine.StartAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!_closing.IsCancellationRequested && ReferenceEquals(_activePane, pane))
            {
                ShowRefusal(error);
            }
        }
    }

    /// <summary>Reads history without waiting for wallet or other optional telemetry.</summary>
    /// <param name="pane">Owning pane.</param><param name="force">Whether to refresh an existing snapshot.</param><returns>The history read.</returns>
    private async Task ReadPaneHistoryAsync(SessionPaneState pane, bool force = false)
    {
        if (pane.Engine.History.Initialized && !force)
        {
            DrawHistoryRead();
            return;
        }

        try
        {
            await pane.Engine.LoadHistoryAsync(_closing.Token).ConfigureAwait(true);
            if (ReferenceEquals(_activePane, pane))
            {
                ShowState(pane.Engine.State);
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (ReferenceEquals(_activePane, pane))
            {
                ShowRefusal(error);
            }
        }
    }

    /// <summary>Marshals history activity to the window dispatcher.</summary>
    private void OnHistoryChanged() => Dispatcher.UIThread.Post(DrawHistoryRead);

    /// <summary>Refreshes canonical status and metrics after relevant runtime events.</summary>
    /// <param name="engine">Owning runtime.</param><param name="frame">Accepted event.</param>
    private void OnPaneFrame(SessionEngine engine, Contracts.Events.WireFrame frame) => Dispatcher.UIThread.Post(() =>
    {
        var pane = _navigation?.Panes.FirstOrDefault(p => ReferenceEquals(p.Engine, engine));
        if (pane is null || _closing.IsCancellationRequested)
        {
            return;
        }

        if (frame.Kind == "turn_done" && !ReferenceEquals(_activePane, pane))
        {
            pane.Unread = true;
            DrawPaneTabs();
        }
        else if (frame.Kind == "turn_done" && IsActive && ConversationArea.IsEffectivelyVisible)
        {
            _ = MarkPaneViewedAsync(pane);
        }

        if (frame.Kind is "usage" or "turn_started" or "turn_done" or "approval_request" or "ask_request")
        {
            _ = ReadPaneStatusAsync(pane);
        }

        if (ReferenceEquals(_engine, engine) && frame.Kind is "usage" or "turn_done")
        {
            _ = LoadComposeTelemetryAsync();
        }
    });

    /// <summary>Refreshes canonical history after an unrecoverable event gap.</summary>
    /// <param name="engine">Owning runtime.</param>
    private void OnPaneGap(SessionEngine engine) => Dispatcher.UIThread.Post(async () =>
    {
        var pane = _navigation?.Panes.FirstOrDefault(p => ReferenceEquals(p.Engine, engine));
        if (pane is null || _closing.IsCancellationRequested)
        {
            return;
        }

        await Task.WhenAll(ReadPaneHistoryAsync(pane, true), ReadPaneStatusAsync(pane)).ConfigureAwait(true);
    });

    /// <summary>Shows preceding-page and retry actions for the active history window.</summary>
    private void DrawHistoryRead()
    {
        var history = _engine?.History;
        EarlierHistory.IsVisible = history is { HasMore: true } or { Busy: true } or { Failed: true };
        EarlierHistory.IsEnabled = history?.Busy != true;
        EarlierHistory.Content = MessageCatalog.Get(history?.Busy == true ? "common.loading" : history?.Failed == true ? "common.retry" : "chat.load_earlier_messages", Language);
    }

    /// <summary>Prepends history while retaining the current reading position.</summary>
    /// <returns>The earlier-page read.</returns>
    private async Task ReadEarlierHistoryAsync()
    {
        if (_engine is not { } engine)
        {
            return;
        }

        var scroll = Conversation.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        var height = scroll?.Extent.Height ?? 0;
        var offset = scroll?.Offset ?? default;
        try
        {
            if (engine.History.HasMore)
            {
                await engine.LoadEarlierHistoryAsync(_closing.Token).ConfigureAwait(true);
            }
            else
            {
                await engine.LoadHistoryAsync(_closing.Token).ConfigureAwait(true);
            }

            if (!ReferenceEquals(_engine, engine))
            {
                return;
            }

            Conversation.Show(engine.State);
            Dispatcher.UIThread.RunJobs();
            if (scroll is not null)
            {
                scroll.Offset = offset.WithY(offset.Y + Math.Max(0, scroll.Extent.Height - height));
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (ReferenceEquals(_engine, engine))
            {
                ShowRefusal(error);
            }
        }
    }

    /// <summary>Offers an explicit recent-history copy after a replay-limit refusal.</summary>
    /// <param name="target">The oversized canonical transcript.</param>
    private void OfferSessionTail(SessionOpenTarget target)
    {
        var offer = new Flyout();
        var accept = new Button { Content = MessageCatalog.Get("session.continue_from_the_most_recent", Language) };
        accept.Click += async (_, _) =>
        {
            offer.Hide();
            try
            {
                var source = await _navigation!.OpenAsync(
                    target with
                    {
                        SessionPath = string.Empty,
                    },
                    false,
                    NewTaskPreset,
                    _closing.Token).ConfigureAwait(true);
                var tail = await source.Engine.TailAsync(target.SessionPath, _closing.Token).ConfigureAwait(true);
                await OpenTargetAsync(target with
                {
                    SessionPath = tail.Path,
                }).ConfigureAwait(true);
                ShowNote(MessageCatalog.Get("session.saved_the_most_recent_part", Language, ("n", tail.Dropped.ToString(System.Globalization.CultureInfo.CurrentCulture))));
            }
            catch (Exception error)
            {
                ShowRefusal(error);
            }
        };
        var cancel = new Button { Content = MessageCatalog.Get("common.cancel", Language) };
        cancel.Click += (_, _) => offer.Hide();
        offer.Content = new StackPanel { MaxWidth = 360, Spacing = 8, Children = { new TextBlock { Text = MessageCatalog.Get("session.this_sessions_history_is_over", Language), TextWrapping = Avalonia.Media.TextWrapping.Wrap }, accept, cancel } };
        offer.ShowAt(PaneTabs.IsVisible ? PaneTabs : TitleChrome);
    }

    /// <summary>Rejects late status reads and updates only their owning pane.</summary>
    /// <param name="pane">Owning pane.</param><returns>The independent status read.</returns>
    private async Task ReadPaneStatusAsync(SessionPaneState pane)
    {
        var ticket = ++pane.StatusRead;
        var revision = pane.Engine.Generation.Revision;
        try
        {
            var status = await pane.Engine.StatusAsync(_closing.Token).ConfigureAwait(true);
            if (status is null || _closing.IsCancellationRequested || ticket != pane.StatusRead)
            {
                return;
            }

            var title = Projects.SearchEntries.FirstOrDefault(entry => entry.Target.Host == pane.Host && RailPresenter.Same(entry.Target.SessionPath, status.SessionPath)).Session?.Title;
            if (!string.IsNullOrWhiteSpace(title))
            {
                status = status with
                {
                    Label = title,
                };
            }

            pane.Status = status;
            pane.Engine.RestoreRunning(status.Running, revision);
            if (ReferenceEquals(_activePane, pane))
            {
                if (status.SessionPath != _sessionPath)
                {
                    SaveDraft();
                    AdoptSessionPath(status.SessionPath);
                    AdoptComposer(status);
                }
                else
                {
                    _composerStatus = status;
                    ShowComposeStatus(status);
                }

                CrumbTitle.Text = status.Label.Length > 0 ? status.Label : MessageCatalog.Get("common.new_session", Language);
                DrawComposeMeters(pane.Engine.State);
            }

            DrawPaneTabs();
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (ReferenceEquals(_activePane, pane))
            {
                ShowRefusal(error);
            }
        }
    }

    /// <summary>Requires confirmation before closing a pane with running work.</summary>
    /// <param name="panes">Explicitly selected panes.</param><returns>The confirmation or close.</returns>
    private async Task ClosePanesAsync(SessionPaneState[] panes)
    {
        if (_navigation is null)
        {
            return;
        }

        if (panes.Any(p => p.Engine.State.Running))
        {
            var confirm = new Flyout();
            var accept = new Button { Content = MessageCatalog.Get("session.close_conversation", Language) };
            accept.Click += async (_, _) =>
            {
                confirm.Hide();
                await CloseSettledPanesAsync(panes).ConfigureAwait(true);
            };
            var cancel = new Button { Content = MessageCatalog.Get("common.cancel", Language) };
            cancel.Click += (_, _) => confirm.Hide();
            confirm.Content = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = string.Join("、", panes.Where(p => p.Engine.State.Running).Select(p => p.Status.Label)) + MessageCatalog.Get("app.closing_stops_it", Language) }, accept, cancel } };
            confirm.ShowAt(PaneTabs);
            return;
        }

        await CloseSettledPanesAsync(panes).ConfigureAwait(true);
    }

    /// <summary>Closes acknowledged panes and focuses a surviving conversation.</summary>
    /// <param name="panes">Explicitly selected panes.</param><returns>The acknowledged closes.</returns>
    private async Task CloseSettledPanesAsync(SessionPaneState[] panes)
    {
        try
        {
            SaveDraft();
            foreach (var pane in panes)
            {
                await _navigation!.CloseAsync(pane, _closing.Token).ConfigureAwait(true);
            }

            var next = _navigation!.Panes.LastOrDefault();
            if (next is not null && panes.Contains(_activePane))
            {
                await NavigatePaneAsync(next).ConfigureAwait(true);
            }
            else if (next is null)
            {
                await OpenTargetAsync(new SessionOpenTarget(_sidebarHost, _workspaceRoot, string.Empty)).ConfigureAwait(true);
            }

            DrawPaneTabs();
            await LoadTreeAsync().ConfigureAwait(true);
        }
        catch (Exception error)
        {
            ShowRefusal(error);
        }
    }
}
