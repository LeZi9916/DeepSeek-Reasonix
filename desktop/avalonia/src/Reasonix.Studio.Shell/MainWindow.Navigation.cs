using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Diagnostics;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.Shell.Session;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Owns all runtimes retained by this window.</summary>
    private WindowSessionCoordinator? _navigation;

    /// <summary>The pane currently mounted in the conversation surface.</summary>
    private SessionPaneState? _activePane;

    /// <summary>Peer-thread state by canonical session path.</summary>
    private IReadOnlyDictionary<string, PeerTabMark> _peerMarks = new Dictionary<string, PeerTabMark>(StringComparer.Ordinal);

    /// <summary>Guards overlapping peer-state reads.</summary>
    private bool _peerFetching;

    /// <summary>Orders all session navigation through one canonical target.</summary>
    /// <param name="target">Host, workspace and transcript identity.</param><returns>The latest visible navigation.</returns>
    internal Task OpenTargetAsync(SessionOpenTarget target) => NavigateAsync(target, null);

    /// <summary>Recognizes the typed tool result that can add a job to the status endpoint.</summary>
    /// <param name="frame">The admitted kernel frame.</param>
    /// <returns>Whether the job list should be read again.</returns>
    private static bool StartsBackgroundJob(Contracts.Events.WireFrame frame) =>
        !frame.IsReplay && frame.Kind == "tool_result" && frame.Payload.ValueKind == JsonValueKind.Object
        && frame.Payload.TryGetProperty("tool", out var tool) && tool.ValueKind == JsonValueKind.Object
        && tool.TryGetProperty("execution", out var execution) && execution.ValueKind == JsonValueKind.Object
        && execution.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String
        && state.GetString() == "background_started";

    /// <summary>Orders retained-tab activation through the same navigation ticket as every other entry.</summary>
    /// <param name="pane">The explicitly selected retained pane.</param><returns>The acknowledged navigation.</returns>
    private Task NavigatePaneAsync(SessionPaneState pane) => NavigateAsync(new SessionOpenTarget(pane.Host, pane.Status.WorkspaceRoot, pane.Status.SessionPath), pane);

    /// <summary>Preserves the current page until the latest requested history is readable.</summary>
    /// <param name="target">Canonical target.</param>
    /// <param name="retained">Optional already open pane.</param>
    /// <param name="refreshTree">Whether navigation owns the sidebar refresh.</param>
    /// <returns>The navigation result.</returns>
    private async Task NavigateAsync(SessionOpenTarget target, SessionPaneState? retained, bool refreshTree = true)
    {
        using var timing = StudioPerformance.Begin(target.SessionPath.Length == 0 ? "workspace.open" : "history.open");
        var identity = System.Text.Json.JsonSerializer.Serialize(target);
        var ticket = _switch.Begin(identity);
        NavigationProgress.IsVisible = true;
        try
        {
            await OpenTargetCoreAsync(target, false, () => _switch.IsCurrent(ticket), retained, refreshTree: refreshTree).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!_closing.IsCancellationRequested && _switch.IsCurrent(ticket))
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
            if (!_closing.IsCancellationRequested && _switch.IsCurrent(ticket))
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
        _navigation.StatePresented += OnChanged;
        _navigation.FrameReceived += OnPaneFrame;
        _navigation.HistoryChanged += OnHistoryChanged;
        _navigation.HistoryInvalidated += OnPaneGap;
        _navigation.QueueChanged += OnQueueChanged;
        _navigation.TreeChanged += OnTreeChanged;
        _navigation.StreamFailed += ShowRefusal;
        _navigation.RefreshParkedStatus = pane => ReadPaneStatusAsync(pane);
        _navigation.RefreshParkedHistory = pane => ReadPaneHistoryAsync(pane, true);
        _navigation.StartStreamScheduling();
        _navigation.StreamLimit = _workspacePreferences.LiveStreamLimit ?? 3;
        _navigation.Closed += pane =>
        {
            ReleaseWorkbenchFor(pane.Engine);
            ReconcilePaneStreams();
        };
        _navigation.Folded += title => ShowNote(MessageCatalog.Get("session.the_open_session_limit_n", Language, ("n", _navigation.Limit.ToString(System.Globalization.CultureInfo.CurrentCulture)), ("title", title)));
    }

    /// <summary>Connects pane and history actions to the shared coordinator.</summary>
    private void InitializeNavigation()
    {
        Activated += async (_, _) =>
        {
            if (_activePane is { } pane && ConversationArea.IsEffectivelyVisible)
            {
                if (RememberUnreadSince(pane))
                {
                    Conversation.RefreshNewAnswers();
                }

                await MarkPaneViewedAsync(pane).ConfigureAwait(true);
            }
        };
        EarlierHistory.Click += async (_, _) => await ReadEarlierHistoryAsync().ConfigureAwait(true);
        Conversation.EarlierHistoryRequested += async () => await ReadEarlierHistoryAsync().ConfigureAwait(true);
        Projects.NavigateRequested += async target => await OpenTargetAsync(target).ConfigureAwait(true);
        PaneTabs.FocusRequested += async pane => await NavigatePaneAsync(pane).ConfigureAwait(true);
        PaneTabs.CloseRequested += async panes => await ClosePanesAsync(panes).ConfigureAwait(true);
        PaneTabs.RenameRequested += pane => Projects.RequestAction(new WorkspaceAction("session.rename-start", new SessionOpenTarget(pane.Host, pane.Status.WorkspaceRoot, pane.Status.SessionPath), new Contracts.Tree.TreeSession { Path = pane.Status.SessionPath, Title = pane.Status.Label }));
        PaneTabs.PeerOpenRequested += async path => await OpenPeerThreadAsync(path).ConfigureAwait(true);
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
        Conversation.DisclosureChoices = _activePane.DisclosureChoices;
        BindAnswerBoundary(_activePane);
        _navigation.Focused = _activePane;
        _navigation.Bind(engine);
        DrawPaneTabs();
        _ = RefreshPeerMarksAsync();
    }

    /// <summary>Opens a peer thread by its canonical session path.</summary>
    /// <param name="path">Peer session path.</param>
    /// <returns>The navigation task.</returns>
    private async Task OpenPeerThreadAsync(string path)
    {
        var target = Projects.SearchEntries
            .Where(entry => RailPresenter.Same(entry.Target.SessionPath, path))
            .Select(entry => entry.Target)
            .FirstOrDefault();
        if (target is not null)
        {
            await OpenTargetAsync(target).ConfigureAwait(true);
        }
    }

    /// <summary>Updates tab titles and activity without replacing runtime state.</summary>
    private void DrawPaneTabs()
    {
        ReflectUnreadBadge();
        if (_workspacePreferences.ShowSessionTabs)
        {
            PaneTabs.Show(_navigation?.Panes ?? [], _activePane, Language, _peerMarks);
        }
        else
        {
            PaneTabs.IsVisible = false;
        }
    }

    /// <summary>Reads peer-thread marks for the retained session paths.</summary>
    /// <returns>The mark refresh.</returns>
    private async Task RefreshPeerMarksAsync()
    {
        if (_peerFetching || _engine is not { } engine)
        {
            return;
        }

        _peerFetching = true;
        try
        {
            var state = await engine.Pages.PeerStateAsync(_closing.Token).ConfigureAwait(true);
            if (_closing.IsCancellationRequested || !ReferenceEquals(_engine, engine))
            {
                return;
            }

            var next = new Dictionary<string, PeerTabMark>(StringComparer.Ordinal);
            if ((bool?)state?["enabled"] == true && state?["threads"] is JsonArray threads)
            {
                foreach (var thread in threads.OfType<JsonObject>())
                {
                    if ((string?)thread["sessionPath"] is { Length: > 0 } path && (string?)thread["state"] is { } mark)
                    {
                        next[path] = new PeerTabMark(mark, (string?)thread["title"] ?? string.Empty, (string?)thread["project"] ?? string.Empty);
                    }
                }
            }

            if (next.Count != _peerMarks.Count || next.Any(pair => !_peerMarks.TryGetValue(pair.Key, out var old) || old != pair.Value))
            {
                _peerMarks = next;
                DrawPaneTabs();
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            _peerFetching = false;
        }
    }

    /// <summary>Locates a runtime and focuses it only for the current navigation.</summary>
    /// <param name="target">Canonical target.</param><param name="register">Whether to register the project.</param>
    /// <param name="current">Checks the navigation ticket.</param><param name="retained">Optional already open pane.</param>
    /// <param name="animateSetupDeparture">Whether opening the workspace completes setup.</param>
    /// <param name="refreshTree">Whether to schedule a general sidebar refresh.</param>
    /// <returns>The acknowledged navigation.</returns>
    private async Task OpenTargetCoreAsync(SessionOpenTarget target, bool register, Func<bool>? current = null, SessionPaneState? retained = null, bool animateSetupDeparture = false, bool refreshTree = true)
    {
        bool IsCurrent() => !_closing.IsCancellationRequested && (current is null || current());

        if (_engine is not { } previous || !IsCurrent())
        {
            return;
        }

        var started = StudioPerformance.Timestamp();
        EnsureNavigation(_launchOwner ?? previous);
        if (_activePane is null)
        {
            _activePane = _navigation!.Capture(previous, _composerStatus);
            Conversation.DisclosureChoices = _activePane.DisclosureChoices;
            BindAnswerBoundary(_activePane);
        }

        _navigation!.Focused = _activePane;
        var openStarted = StudioPerformance.Timestamp();
        var pane = retained ?? await _navigation.OpenAsync(target, register, NewTaskPreset, _closing.Token).ConfigureAwait(true);
        StudioPerformance.Record("navigation.runtime-open", openStarted);
        if (!IsCurrent())
        {
            return;
        }

        var history = pane.Engine.History.Initialized ? Task.CompletedTask : pane.Engine.LoadHistoryAsync(_closing.Token);
        var refreshStatus = pane.StatusConfirmed;
        var status = refreshStatus ? Task.CompletedTask : ReadPaneStatusAsync(pane, required: true);
        if (refreshStatus)
        {
            _navigation.EnsureWatching(pane);
        }

        await Task.WhenAll(history, status).ConfigureAwait(true);
        if (!IsCurrent())
        {
            return;
        }

        var title = Projects.SearchEntries.FirstOrDefault(entry => entry.Target.Host == target.Host && RailPresenter.Same(entry.Target.SessionPath, target.SessionPath)).Session?.Title;
        if (!string.IsNullOrWhiteSpace(title))
        {
            pane.Status = pane.Status with
            {
                Label = title,
            };
        }

        await FocusPaneAsync(pane, animateSetupDeparture, IsCurrent, refreshStatus, refreshTree).ConfigureAwait(true);
        if (started != 0 && IsCurrent())
        {
            RequestAnimationFrame(_ =>
            {
                if (IsCurrent() && ReferenceEquals(_activePane, pane) && ConversationArea.IsEffectivelyVisible)
                {
                    StudioPerformance.Record("navigation.viewport-tick", started);
                }
            });
        }
    }

    /// <summary>Mounts a retained pane and independently reads its history and status.</summary>
    /// <param name="pane">Pane to focus.</param>
    /// <param name="animateSetupDeparture">Whether mounting this pane completes setup.</param>
    /// <param name="current">Whether this navigation still owns the view.</param>
    /// <param name="refreshStatus">Whether a retained status should refresh after the pane is usable.</param>
    /// <param name="refreshTree">Whether to schedule a general sidebar refresh.</param>
    /// <returns>The mounted session.</returns>
    private async Task FocusPaneAsync(SessionPaneState pane, bool animateSetupDeparture, Func<bool> current, bool refreshStatus, bool refreshTree)
    {
        if (_engine is null || (ReferenceEquals(_activePane, pane) && _sessionPath == pane.Status.SessionPath))
        {
            if (animateSetupDeparture && SetupPanel.IsVisible)
            {
                await ReturnToConversationAsync(animateSetupDeparture: true, current).ConfigureAwait(true);
            }
            else
            {
                Composer.Focus();
            }

            return;
        }

        DisarmStop();
        SaveDraft();
        if (_activePane is { } previous && !ReferenceEquals(previous, pane))
        {
            previous.ScrollAnchor = Conversation.ReadSessionPosition();
            previous.WorkbenchOpen = WorkbenchPanel.IsVisible;
            previous.FilePath = SelectedWorkbenchDocument;
        }

        _launchOwner ??= _engine;
        _engine = pane.Engine;
        _activePane = pane;
        _navigation!.Focused = pane;
        pane.Unread = false;
        _sidebarHost = pane.Host;
        _treeRead++;
        Conversation.DisclosureChoices = pane.DisclosureChoices;
        BindAnswerBoundary(pane);
        RememberUnreadSince(pane);
        ComposerPlan.Reset();
        AdoptSessionPath(pane.Status.SessionPath);
        AdoptComposer(pane.Status);
        CrumbTitle.Text = pane.Status.Label.Length > 0 ? pane.Status.Label : MessageCatalog.Get("common.new_session", Language);
        ShowState(pane.Engine.State);
        if (SettingsPanel.IsVisible || SetupPanel.IsVisible || SearchResultsPanel.IsVisible || !LayoutRoot.IsVisible)
        {
            await ReturnToConversationAsync(animateSetupDeparture, current).ConfigureAwait(true);
        }
        else
        {
            Composer.Focus();
        }

        if (!current() || !ReferenceEquals(_activePane, pane))
        {
            return;
        }

        DrawHistoryRead();
        Conversation.RestoreSessionPosition(pane.ScrollAnchor);
        DrawPaneTabs();
        _navigation!.EnsureWatching(pane);
        Dispatcher.UIThread.Post(() => _ = RefreshFocusedPaneAsync(pane, current, refreshStatus, refreshTree), DispatcherPriority.Background);
    }

    /// <summary>Refreshes secondary workspace surfaces after the conversation can receive input.</summary>
    /// <param name="pane">The mounted session.</param>
    /// <param name="current">Whether this navigation still owns the view.</param>
    /// <param name="refreshStatus">Whether the retained status should refresh.</param>
    /// <param name="refreshTree">Whether navigation owns the sidebar refresh.</param>
    /// <returns>The secondary refresh.</returns>
    private async Task RefreshFocusedPaneAsync(SessionPaneState pane, Func<bool> current, bool refreshStatus, bool refreshTree)
    {
        if (!current() || !ReferenceEquals(_activePane, pane))
        {
            return;
        }

        try
        {
            await Task.WhenAll(
                _navigation!.AcknowledgeReadAsync(pane, _closing.Token),
                MarkPaneViewedAsync(pane),
                refreshStatus ? ReadPaneStatusAsync(pane) : Task.CompletedTask,
                refreshTree ? LoadTreeAsync() : Task.CompletedTask,
                LoadQueueAsync(),
                RestoreWorkbenchFocusAsync(pane)).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (current() && ReferenceEquals(_activePane, pane))
            {
                ShowRefusal(error);
            }
        }
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
    private void OnHistoryChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (!_closing.IsCancellationRequested)
        {
            DrawHistoryRead();
        }
    });

    /// <summary>Refreshes canonical status and metrics after relevant runtime events.</summary>
    /// <param name="engine">Owning runtime.</param><param name="frame">Accepted event.</param>
    private void OnPaneFrame(SessionEngine engine, Contracts.Events.WireFrame frame)
    {
        var kind = frame.Kind;
        var status = kind is "usage" or "turn_started" or "turn_done" or "approval_request" or "ask_request";
        var compaction = kind == "notice" && frame.Payload.ValueKind == System.Text.Json.JsonValueKind.Object
            && frame.Payload.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String && code.GetString() is "compacted" or "compact_declined" or "compact_failed";
        var backgroundJob = StartsBackgroundJob(frame);
        var refreshBrowser = kind is "browser_tabs_changed" or "turn_done";
        if (!status && !compaction && !backgroundJob && !refreshBrowser && kind != "inbox_changed")
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            var pane = _navigation?.Panes.FirstOrDefault(p => ReferenceEquals(p.Engine, engine));
            if (pane is null || _closing.IsCancellationRequested)
            {
                return;
            }

            if (kind == "turn_done" && !ReferenceEquals(_activePane, pane))
            {
                pane.Unread = true;
                DrawPaneTabs();
            }
            else if (kind == "turn_done" && IsActive && ConversationArea.IsEffectivelyVisible)
            {
                _ = MarkPaneViewedAsync(pane);
            }

            if (compaction)
            {
                _compactOperations.Remove(engine);
            }

            if (status
                || (kind == "inbox_changed" && pane.Status?.Jobs is { Length: > 0 })
                || backgroundJob)
            {
                _ = ReadPaneStatusAsync(pane);
            }

            if (refreshBrowser && ReferenceEquals(_engine, engine))
            {
                _ = RefreshAgentBrowserTabsAsync();
            }

            if (ReferenceEquals(_engine, engine) && kind is "usage" or "turn_done")
            {
                _ = LoadComposeTelemetryAsync();
            }
        });
    }

    /// <summary>Refreshes canonical history after an unrecoverable event gap.</summary>
    /// <param name="engine">Owning runtime.</param>
    private void OnPaneGap(SessionEngine engine) => Dispatcher.UIThread.Post(async () =>
    {
        var pane = _navigation?.Panes.FirstOrDefault(p => ReferenceEquals(p.Engine, engine));
        if (pane is null || _closing.IsCancellationRequested)
        {
            return;
        }

        _compactOperations.Remove(engine);
        await Task.WhenAll(ReadPaneHistoryAsync(pane, true), ReadPaneStatusAsync(pane)).ConfigureAwait(true);
    });

    /// <summary>Shows preceding-page and retry actions for the active history window.</summary>
    private void DrawHistoryRead()
    {
        var history = _engine?.History;
        EarlierHistory.IsVisible = history is { HasMore: true } or { Busy: true } or { Failed: true };
        EarlierHistory.IsEnabled = history?.Busy != true;
        Conversation.SetHeroHistoryOutstanding(EarlierHistory.IsVisible);
        Conversation.SetMessageRailHistory(history?.Earlier ?? 0, history is { HasMore: true, Busy: false, Failed: false });
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

        using var timing = StudioPerformance.Begin("history.earlier");
        var started = StudioPerformance.Timestamp();
        Conversation.PauseTranscriptFollow();
        var anchor = Conversation.ReadAnchor();
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

            if (_closing.IsCancellationRequested || !ReferenceEquals(_engine, engine))
            {
                return;
            }

            var state = engine.State;
            if (!ReferenceEquals(Conversation.State, state))
            {
                PresentState(state, Conversation.State);
            }

            Conversation.RestoreAnchor(anchor, applied: started == 0 ? null : () => StudioPerformance.Record("history.anchor-restored", started));
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
    /// <param name="pane">Owning pane.</param>
    /// <param name="required">Whether opening the target depends on the status.</param>
    /// <returns>The independent status read.</returns>
    private async Task ReadPaneStatusAsync(SessionPaneState pane, bool required = false)
    {
        if (_closing.IsCancellationRequested || (!required && !pane.StatusConfirmed))
        {
            return;
        }

        var ticket = ++pane.StatusRead;
        var revision = pane.Engine.Generation.Revision;
        try
        {
            var status = await pane.Engine.StatusAsync(_closing.Token).ConfigureAwait(true);
            if (_closing.IsCancellationRequested || ticket != pane.StatusRead)
            {
                return;
            }

            if (status is null)
            {
                if (required)
                {
                    throw new InvalidDataException("The workspace did not return session status.");
                }

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
            pane.StatusConfirmed = true;
            pane.Engine.RestoreRunning(status.Running, revision);
            if (!required && ReferenceEquals(_activePane, pane))
            {
                if (status.SessionPath != _sessionPath)
                {
                    SaveDraft();
                    AdoptSessionPath(status.SessionPath);
                    AdoptComposer(status);
                    BindAnswerBoundary(pane);
                    ObserveUnreadSpan();
                    Conversation.RefreshNewAnswers();
                }
                else
                {
                    _composerStatus = status;
                    ShowComposeStatus(status);
                }

                CrumbTitle.Text = status.Label.Length > 0 ? status.Label : MessageCatalog.Get("common.new_session", Language);
                DrawComposeMeters(pane.Engine.State);
            }

            if (!required)
            {
                DrawPaneTabs();
                ReconcilePaneStreams();
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch (Exception error) when (!required)
        {
            if (!_closing.IsCancellationRequested && ReferenceEquals(_activePane, pane))
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
