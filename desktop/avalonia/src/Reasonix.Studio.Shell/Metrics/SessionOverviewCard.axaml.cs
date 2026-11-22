using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Reasonix.Studio.Contracts.Wire;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Globalization;
using System.Linq;

namespace Reasonix.Studio.Shell.Metrics;

/// <summary>The same session ledger displayed beside the cache indicator.</summary>
public sealed partial class SessionOverviewCard : UserControl
{
    /// <summary>Redraws programmatic source brushes once popup resources are available.</summary>
    private Action? _refresh;

    /// <summary>Initializes a new instance of the <see cref="SessionOverviewCard"/> class.</summary>
    public SessionOverviewCard()
    {
        InitializeComponent();
        Context.Click += (_, _) => OpenContextRequested?.Invoke();
        AttachedToVisualTree += (_, _) => _refresh?.Invoke();
        ActualThemeVariantChanged += (_, _) => _refresh?.Invoke();
    }

    /// <summary>Raised to replace this card with context details.</summary>
    public event Action? OpenContextRequested;

    /// <summary>Projects accounting from its canonical ledger and covered quote.</summary>
    /// <param name="usage">Canonical usage.</param><param name="quote">Canonical cost.</param>
    /// <param name="hidden">Whether amounts are masked.</param><param name="hasContext">Whether context can be opened.</param>
    /// <param name="language">Interface language.</param>
    public void Show(SessionUsage? usage, CostQuote? quote, bool hidden, bool hasContext, InterfaceLanguage language)
    {
        _refresh = () => Show(usage, quote, hidden, hasContext, language);
        string Say(string key) => MessageCatalog.Get(key, language);
        Title.Text = Say("session.session_overview");
        Since.Text = Say("session.since_this_session_was_opened");
        Empty.Text = Say("session.this_session_has_no_billed");
        var available = usage is { Requests: > 0 };
        Empty.IsVisible = !available;
        Values.IsVisible = Footer.IsVisible = Divider.IsVisible = available;
        Sources.IsVisible = available && usage?.Sources.Length > 0;
        Sources.Children.Clear();
        if (usage is null || !available)
        {
            return;
        }

        RequestsLabel.Text = Say("common.requests");
        Requests.Text = usage.Requests.ToString(CultureInfo.CurrentCulture);
        var main = usage.Sources.FirstOrDefault(source => source.Source == "executor")?.Requests ?? 0;
        var sub = usage.Sources.FirstOrDefault(source => source.Source == "subagent")?.Requests ?? 0;
        RequestsNote.Text = main + sub > 0 ? MessageCatalog.Get("session.main_n_sub_m", language, ("a", main.ToString(CultureInfo.CurrentCulture)), ("b", sub.ToString(CultureInfo.CurrentCulture))) : Say("session.model_responses_that_reported_usage");
        CostLabel.Text = Say("session.cost");
        Cost.Text = MetricReadouts.Cost(quote, hidden, language);
        CostNote.Text = quote?.Coverage == "partial" ? Say("session.some_requests_carried_no_price") : quote?.Coverage == "incomplete" ? Say("session.price_unknown") : quote?.Coverage == "none" || quote is null ? Say("session.nothing_billed_yet") : string.Empty;
        CostNote.IsVisible = CostNote.Text.Length > 0;
        TokensLabel.Text = Say("Token");
        Tokens.Text = MetricReadouts.Tokens(usage.PromptTokens + usage.CompletionTokens);
        TokensNote.Text = MessageCatalog.Get("session.in_a_out_b", language, ("a", MetricReadouts.Tokens(usage.PromptTokens)), ("b", MetricReadouts.Tokens(usage.CompletionTokens)));
        CacheLabel.Text = Say("common.cache_hit");
        Cache.Text = MetricReadouts.Percent(MetricReadouts.CacheRate(usage.CacheHit, usage.CacheMiss, usage.CacheUnknown));
        CacheNote.Text = usage.CacheUnknown > 0 ? MessageCatalog.Get("session.hit_hit_miss_leaves_out", language, ("n", MetricReadouts.Tokens(usage.CacheUnknown))) : Say("session.hit_hit_miss");
        WarmLabel.Text = Say("session.warm_cache_hit");
        var cold = usage.ColdStart;
        Warm.Text = MetricReadouts.Percent(cold is null ? null : MetricReadouts.CacheRate(usage.CacheHit - cold.CacheHit, usage.CacheMiss - cold.CacheMiss, usage.CacheUnknown - cold.CacheUnknown));
        WarmNote.Text = cold is null ? Say("session.leaves_out_the_cold_start") : MessageCatalog.Get("session.leaves_out_the_cold_start_n", language, ("n", MetricReadouts.Tokens(cold.PromptTokens)));
        RatioLabel.Text = Say("session.output_ratio");
        Ratio.Text = usage.PromptTokens > 0 ? (100d * usage.CompletionTokens / usage.PromptTokens).ToString("0.0", CultureInfo.CurrentCulture) + "%" : "—";
        RatioNote.Text = Say("session.output_input");
        WorkTime.Text = Say("session.work_time") + " " + MetricReadouts.WorkTime(usage.WorkMs, language);
        Context.Content = Say("common.context_details");
        Context.IsVisible = hasContext;
        var priced = usage.Sources.Select(source => Amount(source.Cost)).ToArray();
        var total = priced.Sum();
        if (usage.Sources.Length > 0)
        {
            Sources.Children.Add(new TextBlock { Text = Say("common.by_source"), FontWeight = FontWeight.Bold, [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedBrush") });
        }

        foreach (var source in usage.Sources)
        {
            var name = source.Source switch
            {
                "executor" => "session.main_agent",
                "subagent" => "common.subagents",
                "planner" => "session.planning",
                "compaction" => "session.compaction",
                "title" => "session.session_title",
                "classifier" => "session.classifier",
                "goal-evaluator" => "session.goal_evaluation",
                "web-search" => "common.web_search",
                _ => source.Source,
            };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("64,*,Auto"), MinHeight = 26, ColumnSpacing = 8 };
            row.Children.Add(new TextBlock { Text = Say(name), [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextBrush"), VerticalAlignment = VerticalAlignment.Center });
            var bar = new Border { Height = 6, [!Border.BackgroundProperty] = new DynamicResourceExtension("OverlayBrush"), CornerRadius = new CornerRadius(3), ClipToBounds = true };
            var fill = new Border { Height = 6, [!Border.BackgroundProperty] = new DynamicResourceExtension("NetBrush"), HorizontalAlignment = HorizontalAlignment.Left };
            var share = total > 0 ? Math.Max(Amount(source.Cost) > 0 ? 0.02 : 0, Amount(source.Cost) / total) : 0;
            bar.Child = fill;
            bar.SizeChanged += (_, _) => fill.Width = bar.Bounds.Width * share;
            Grid.SetColumn(bar, 1);
            row.Children.Add(bar);
            var cost = new TextBlock { Text = MetricReadouts.Cost(source.Cost, hidden, language), [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedBrush"), MinWidth = 52, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(cost, 2);
            row.Children.Add(cost);
            ToolTip.SetTip(row, source.Requests.ToString(CultureInfo.CurrentCulture) + " · " + MetricReadouts.Tokens(source.PromptTokens + source.CompletionTokens) + " · " + MetricReadouts.Percent(MetricReadouts.CacheRate(source.CacheHit, source.CacheMiss, source.CacheUnknown)));
            Sources.Children.Add(row);
        }
    }

    /// <summary>Returns a chart amount only for an exactly covered source.</summary>
    /// <param name="quote">Canonical source quote.</param><returns>The supported source amount.</returns>
    private static double Amount(CostQuote? quote) => quote?.Coverage == "complete" && double.TryParse((quote.Selected ?? quote.Original)?.Amount, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) ? amount : 0;
}
