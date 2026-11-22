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

    /// <summary>Reads the selected runtime's context and provider wallet.</summary>
    /// <returns>The independent meter reads.</returns>
    private async Task LoadComposeTelemetryAsync()
    {
        var engine = _engine;
        var draft = _draftKey;
        var ticket = ++_composeMeterRead;
        _composeWallet = null;
        _composeContext = null;
        DrawComposeWallet();
        if (engine is null)
        {
            return;
        }

        await Task.WhenAll(ReadContext(), ReadWallet()).ConfigureAwait(true);
        async Task ReadContext()
        {
            try
            {
                var context = await engine.Pages.ContextAsync(_closing.Token).ConfigureAwait(true);
                if (ticket == _composeMeterRead && engine == _engine && draft == _draftKey)
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
                if (ticket == _composeMeterRead && engine == _engine && draft == _draftKey)
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
        CacheOverviewText.Text = MessageCatalog.Get("composer.hit_hit_miss_miss", Language, ("hit", state.Metrics.Hit.ToString(CultureInfo.CurrentCulture)), ("miss", state.Metrics.Miss.ToString(CultureInfo.CurrentCulture)))
            + Environment.NewLine + MessageCatalog.Get("composer.run_output", Language) + ": " + state.Metrics.Out.ToString(CultureInfo.CurrentCulture);
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
