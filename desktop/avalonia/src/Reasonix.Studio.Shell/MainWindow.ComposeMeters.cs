using Avalonia.Controls;
using Avalonia.Media;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.State;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The current provider's wallet reading.</summary>
    private JsonObject? _composeWallet;

    /// <summary>The context breakdown used by the meter until a newer usage event.</summary>
    private JsonObject? _composeContext;

    /// <summary>Whether this window masks displayed amounts.</summary>
    private bool _hideAmounts;

    /// <summary>Rejects superseded context and wallet reads.</summary>
    private long _composeMeterRead;

    /// <summary>The runtime owning the retained readings.</summary>
    private Kernel.Session.SessionEngine? _composeMeterOwner;

    /// <summary>Reads the selected runtime's context and provider wallet.</summary>
    /// <returns>The independent meter reads.</returns>
    private async Task LoadComposeTelemetryAsync()
    {
        var engine = _engine;
        var draft = DraftKey;
        var ticket = ++_composeMeterRead;
        if (!ReferenceEquals(_composeMeterOwner, engine))
        {
            _composeMeterOwner = engine;
            _composeWallet = null;
            _composeContext = null;
            _composeServers = null;
            _composeIsolated = false;
        }

        DrawComposeWallet();
        if (engine is null)
        {
            return;
        }

        await Task.WhenAll(ReadContext(), ReadWallet(), ReadExtras()).ConfigureAwait(true);
        async Task ReadExtras()
        {
            await Task.WhenAll(ReadTrace(), ReadMcp(), ReadWorkspace()).ConfigureAwait(true);
        }

        async Task ReadTrace()
        {
            var revision = engine.Generation.Revision;
            try
            {
                var trace = await engine.Pages.TrajectoryAsync(_closing.Token).ConfigureAwait(true);
                engine.Generation.Restore(trace, revision);
                if (ticket == _composeMeterRead && engine == _engine)
                {
                    DrawComposeCards(Conversation.State);
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException) { }
        }

        async Task ReadMcp()
        {
            try
            {
                var servers = await engine.Pages.McpInventoryAsync(_closing.Token).ConfigureAwait(true);
                if (ticket == _composeMeterRead && engine == _engine)
                {
                    _composeServers = servers?["servers"] as System.Text.Json.Nodes.JsonArray;
                    DrawComposeCards(Conversation.State);
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException) { }
        }

        async Task ReadWorkspace()
        {
            try
            {
                var workspace = await engine.Pages.WorkspaceInfoAsync(_closing.Token).ConfigureAwait(true);
                if (ticket == _composeMeterRead && engine == _engine)
                {
                    _composeIsolated = (bool?)workspace?["isolated"] == true && (string?)workspace?["current"] == _workspaceRoot;
                    DrawComposeCards(Conversation.State);
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException) { }
        }

        async Task ReadContext()
        {
            try
            {
                var context = await engine.Pages.ContextAsync(_closing.Token).ConfigureAwait(true);
                if (ticket == _composeMeterRead && engine == _engine && draft == DraftKey)
                {
                    _composeContext = context;
                    DrawComposeMeters(Conversation.State);
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                if (ticket == _composeMeterRead)
                {
                    ToolTip.SetTip(ContextToggle, DescribeError(error));
                }
            }
        }

        async Task ReadWallet()
        {
            try
            {
                var wallet = await engine.Pages.BalanceAsync(_closing.Token).ConfigureAwait(true);
                if (ticket == _composeMeterRead && engine == _engine && draft == DraftKey)
                {
                    _composeWallet = wallet;
                    DrawComposeWallet();
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                if (ticket == _composeMeterRead)
                {
                    ToolTip.SetTip(WalletButton, DescribeError(error));
                }
            }
        }
    }

    /// <summary>Projects accounting without replacing unknown readings with invented values.</summary>
    /// <param name="state">The current conversation fold.</param>
    private void DrawComposeMeters(SessionState state)
    {
        var total = state.Metrics.Hit + state.Metrics.Miss;
        CacheText.Text = total > 0 ? Math.Floor(100d * state.Metrics.Hit / total).ToString(CultureInfo.CurrentCulture) + "%" : "—";
        var ledger = _composerStatus.SessionUsage;
        if (ledger is { Requests: > 0 })
        {
            CacheText.Text = Metrics.MetricReadouts.Percent(Metrics.MetricReadouts.CacheRate(ledger.CacheHit, ledger.CacheMiss, ledger.CacheUnknown));
        }

        var used = state.ContextUsed > 0 ? state.ContextUsed : (long?)_composeContext?["used"] ?? _composerStatus.Used;
        var window = (long?)_composeContext?["window"] ?? _window;
        MeterText.Text = window > 0 ? Math.Min(100, Math.Round(100d * used / window, MidpointRounding.AwayFromZero)).ToString(CultureInfo.CurrentCulture) + "%" : "—";
        ContextReadout.Text = Reasonix.Studio.Pages.Composer.ContextMeter.Readout(used, window, Language);
        ContextBreakdown.Text = _composeContext is null ? string.Empty : string.Join(Environment.NewLine, new[] { ("system", "common.system"), ("tools", "common.tool_definitions"), ("user", "common.input"), ("reply", "chat.find_scope_reply"), ("output", "common.tool_output") }.Select(pair => MessageCatalog.Get(pair.Item2, Language) + ": " + ((long?)_composeContext[pair.Item1] ?? 0).ToString(CultureInfo.CurrentCulture)));
        var fill = window > 0 ? Math.Clamp((double)used / window, 0, 1) : 0;
        var high = window > 0 && Math.Round(fill * 100, MidpointRounding.AwayFromZero) >= 80;
        ContextToggle.Classes.Set("high", high);
        if (fill <= 0)
        {
            ContextArc.Data = null;
        }
        else if (fill >= 1)
        {
            ContextArc.Data = Geometry.Parse("M5.5 1A4.5 4.5 0 1 0 5.5 10A4.5 4.5 0 1 0 5.5 1");
        }
        else
        {
            var angle = (fill * Math.Tau) - (Math.PI / 2);
            ContextArc.Data = Geometry.Parse(FormattableString.Invariant($"M5.5 1A4.5 4.5 0 {(fill > 0.5 ? 1 : 0)} 1 {5.5 + (4.5 * Math.Cos(angle))} {5.5 + (4.5 * Math.Sin(angle))}"));
        }

        LabelComposeLayout();
        DrawComposeCards(state);
    }

    /// <summary>Shows the provider's formatted amount and keeps freshness available.</summary>
    private void DrawComposeWallet()
    {
        WalletButton.IsVisible = _composeWallet is not null;
        WalletText.Text = _hideAmounts ? "•••" : (string?)_composeWallet?["display"];
        AmountMaskSlash.IsVisible = _hideAmounts;
        Label(HideAmounts, _hideAmounts ? "composer.show_amounts" : "composer.hide_amounts");
        ToolTip.SetTip(WalletButton, (bool?)_composeWallet?["stale"] == true ? MessageCatalog.Get("common.stale", Language) : (string?)_composeWallet?["fetchedAt"]);
    }
}
