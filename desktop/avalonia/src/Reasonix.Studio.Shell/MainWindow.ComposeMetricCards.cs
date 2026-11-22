using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.State;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Refreshes live observations without interpolating their numeric values.</summary>
    private readonly DispatcherTimer _composePulse = new() { Interval = TimeSpan.FromMilliseconds(200) };

    /// <summary>Runtimes awaiting a typed compaction result.</summary>
    private readonly HashSet<SessionEngine> _compactOperations = [];

    /// <summary>Canonical server entries used by the context footer.</summary>
    private JsonArray? _composeServers;

    /// <summary>The worktree isolation state acknowledged by the active runtime.</summary>
    private bool _composeIsolated;

    /// <summary>Connects metric cards, keyboard dismissal and kernel maintenance.</summary>
    private void InitializeComposeMetrics()
    {
        CacheHover.CanHover = () => !ContextHover.IsPinned;
        _tariffPulse.Tick += (_, _) => DrawTariff();
        CacheHover.Pinned += () =>
        {
            SpeedHover.Close();
            ContextHover.Close();
        };
        ContextHover.Pinned += () =>
        {
            SpeedHover.Close();
            CacheHover.Close();
        };
        CacheDetails.OpenContextRequested += () =>
        {
            CacheHover.Close();
            ContextToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        ContextDetails.ManageRequested += () =>
        {
            CloseComposeCards();
            ShowSidebarSection("ext");
        };
        ContextDetails.CompactRequested += async () => await CompactContextAsync().ConfigureAwait(true);
        _composePulse.Tick += (_, _) =>
        {
            foreach (var pane in _navigation?.Panes ?? [])
            {
                pane.Engine.Generation.Read(pane.Engine.State.Running);
            }

            DrawComposeCards(Conversation.State);
            if (!Conversation.State.Running && !(_navigation?.Panes.Any(pane => pane.Engine.State.Running) ?? false))
            {
                _composePulse.Stop();
            }
        };
        Closed += (_, _) =>
        {
            _composePulse.Stop();
            _tariffPulse.Stop();
            CloseComposeCards();
        };
    }

    /// <summary>Dismisses details whose content belongs to the previous conversation.</summary>
    private void CloseComposeCards()
    {
        SpeedHover.Close();
        CacheHover.Close();
        ContextHover.Close();
    }

    /// <summary>Projects ledger figures and supported generation timings into native cards.</summary>
    /// <param name="state">Current canonical conversation fold.</param>
    private void DrawComposeCards(SessionState state)
    {
        var reading = _engine?.Generation.Read(state.Running) ?? new GenerationReading(0, 0, 0, 0, 0, [], 1);
        SpeedText.Text = reading.CurrentRate > 0 ? reading.CurrentRate.ToString("0.0", CultureInfo.CurrentCulture) : "—";
        SpeedSpark.Points = reading.Trail;
        SpeedSpark.Peak = reading.Peak;
        SpeedSpark.IsVisible = reading.Trail.Count >= 2;
        SpeedSpark.IsLive = state.Running;
        SpeedLive.Background = this.FindResource(state.Running ? "NetBrush" : "GhostBrush") as IBrush;
        SpeedDetails.Show(reading, state.Running, Language);
        var context = _composeContext?.Deserialize<ContextBreakdown>(JsonSerializerOptions.Web);
        if (context is not null && state.ContextUsed > 0)
        {
            context = context with
            {
                Used = state.ContextUsed,
            };
        }

        ContextDetails.Show(context, _composeServers, state.Running, _engine is { } engine && _compactOperations.Contains(engine), Language);
        CacheDetails.Show(_composerStatus.SessionUsage, _composerStatus.SessionCostQuote, _hideAmounts, context is { Window: > 0 }, Language);
        ToolTip.SetTip(SpeedToggle, null);
        ToolTip.SetTip(CacheToggle, null);
        ToolTip.SetTip(ContextToggle, null);
        var quotedCost = Metrics.MetricReadouts.Cost(_composerStatus.SessionCostQuote, _hideAmounts, Language);
        var sessionCost = _composerStatus.SessionCostQuote is not null ? quotedCost : _hideAmounts ? "•••" : Format.MoneyList(state.Metrics.Cost.Select(amount => ((decimal)amount.Amount, amount.Currency)), Language);
        SessionCostText.IsVisible = _composerStatus.SessionCostQuote is { } quote
            ? quotedCost != "—" || _composerStatus.SessionUsage?.Requests > 0 || quote.Coverage == "incomplete"
            : state.Metrics.Cost.Count > 0;
        SessionCostText.Text = MessageCatalog.Get("common.this_session_2", Language) + " " + sessionCost;
        TurnCostText.IsVisible = SessionCostText.IsVisible && state.Metrics.Turn.Any(amount => amount.Amount > 0);
        TurnCostText.Text = MessageCatalog.Get("composer.this_turn", Language) + " " + (_hideAmounts ? "•••" : Format.MoneyList(state.Metrics.Turn.Select(amount => ((decimal)amount.Amount, amount.Currency)), Language));
        DrawTariff();
        WorkspaceStatus.Show(_sidebarHost, _composeIsolated, _queue?.Items.Count ?? 0, Language);
        if (state.Running && !_composePulse.IsEnabled)
        {
            _composePulse.Start();
        }
    }

    /// <summary>Asks explicitly before submitting maintenance without touching the main draft.</summary>
    /// <returns>The acknowledged maintenance admission.</returns>
    private async Task CompactContextAsync()
    {
        if (_engine is not { } engine || engine.State.Running || _compactOperations.Contains(engine))
        {
            return;
        }

        if (await AskWorkspaceAsync(MessageCatalog.Get("composer.compact_the_current_context", Language), MessageCatalog.Get("composer.the_model_will_continue_from", Language)).ConfigureAwait(true) is null)
        {
            return;
        }

        if (!ReferenceEquals(engine, _engine) || engine.State.Running)
        {
            return;
        }

        _compactOperations.Add(engine);
        DrawComposeCards(engine.State);
        try
        {
            await engine.SubmitAsync("/compact", _closing.Token).ConfigureAwait(true);
        }
        catch (Exception error)
        {
            _compactOperations.Remove(engine);
            ShowRefusal(error);
            DrawComposeCards(engine.State);
        }
    }
}
