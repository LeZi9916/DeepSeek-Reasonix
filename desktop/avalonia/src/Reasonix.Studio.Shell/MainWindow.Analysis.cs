using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Pages;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Host;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The run transition used to refresh a completed trajectory.</summary>
    private bool _analysisWasRunning;

    /// <summary>Rejects stale analysis reads after changing sessions or reopening the view.</summary>
    private long _analysisRead;

    /// <summary>Formats a host duration in seconds.</summary>
    /// <param name="milliseconds">Recorded duration.</param>
    /// <returns>Duration with units.</returns>
    private static string Seconds(double milliseconds) => (milliseconds / 1000d).ToString("0.0", CultureInfo.CurrentCulture) + "s";

    /// <summary>Creates a bordered analytics surface with theme-aware ink.</summary>
    /// <param name="child">Content.</param>
    /// <param name="identity">Captured region.</param>
    /// <param name="padding">Inset.</param>
    /// <returns>The card.</returns>
    private static Border AnalysisCard(Control child, string identity, Thickness padding)
    {
        var card = new Border { Child = child, Padding = padding, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
        card.Bind(Border.BackgroundProperty, card.GetResourceObservable("RaisedBrush"));
        card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("HairBrush"));
        AutomationProperties.SetAutomationId(card, identity);
        return card;
    }

    /// <summary>Places a child in a grid column.</summary>
    /// <param name="grid">Parent grid.</param>
    /// <param name="child">Child.</param>
    /// <param name="column">Column index.</param>
    private static void Place(Grid grid, Control child, int column)
    {
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }

    /// <summary>Reads and presents the current kernel record without depending on the visible transcript.</summary>
    /// <returns>The analysis read.</returns>
    private async Task LoadAnalysisAsync()
    {
        var ticket = ++_analysisRead;
        var draft = _draftKey;
        AnalysisContent.Children.Clear();
        if (_engine is null)
        {
            return;
        }

        try
        {
            var trace = await _engine.Pages.TrajectoryAsync(_closing.Token).ConfigureAwait(true);
            if (ticket != _analysisRead || draft != _draftKey || !AnalysisPanel.IsVisible)
            {
                return;
            }

            DrawTrajectory(trace);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (ticket == _analysisRead && draft == _draftKey)
            {
                ShowRefusal(error);
            }
        }
    }

    /// <summary>Renders the recorded turn, exposing its source for export.</summary>
    /// <param name="trace">The kernel's original trajectory.</param>
    private void DrawTrajectory(JsonObject trace)
    {
        var body = AnalysisContent.Children;
        body.Clear();
        var timeline = RunTimeline.From(trace["events"] as JsonArray ?? new JsonArray());
        if (timeline.Rows.Count == 0)
        {
            body.Add(Detail(MessageCatalog.Get(MessageKey.Panels.ThereIsNoRunTo, Language)));
            return;
        }

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"), ColumnSpacing = 10, Height = 30 };
        AutomationProperties.SetAutomationId(header, "class:run-analysis-head");
        var back = new Button { Content = "‹", Width = 32, Height = 28, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center };
        Label(back, MessageKey.Panels.BackToTheConversation);
        back.Click += (_, _) => ShowAnalysis(false);
        header.Children.Add(back);
        var title = new TextBlock { Text = MessageCatalog.Get(MessageKey.Common.RunAnalysis, Language), [!TextBlock.FontSizeProperty] = StudioTypography.Size(16), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        Place(header, title, 1);
        var note = Detail(MessageCatalog.Get("panels.this_turn_from_recorded_events", Language));
        note.VerticalAlignment = VerticalAlignment.Center;
        Place(header, note, 2);
        var count = Detail(MessageCatalog.Get("panels.n_records", Language, ("n", Format.Count(timeline.Rows.Count, Language))));
        count.VerticalAlignment = VerticalAlignment.Center;
        Place(header, count, 3);
        var export = new Button { Content = MessageCatalog.Get(MessageKey.Panels.ExportRunRecord, Language), [!TextBlock.FontSizeProperty] = StudioTypography.Size(12), Height = 28, Padding = new Thickness(10, 0) };
        export.Click += async (_, _) => await TryAnswerAsync(async () =>
        {
            await new AvaloniaHost(this).SaveTextAsync("reasonix-run.json", trace.ToJsonString(new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(true);
        }).ConfigureAwait(true);
        Place(header, export, 4);
        body.Add(header);

        var activities = timeline.Rows.Where(r => r.Duration is > 0 || r.Recovery).ToArray();
        var rounds = activities.Where(r => r.Bucket == "model" && r.Duration is > 0).OrderBy(r => r.Duration).ToArray();
        var tools = activities.Where(r => r.Bucket == "tool").ToArray();
        var retries = timeline.Rows.Count(r => r.Recovery);
        var total = activities.Sum(r => r.Duration ?? 0);
        var summary = new UniformGrid { Columns = 6 };
        summary.Children.Add(Metric("panels.total_time", Seconds(timeline.Span), "span"));
        summary.Children.Add(Metric("panels.model_p95", rounds.Length == 0 ? "—" : Seconds(rounds[(int)Math.Ceiling(rounds.Length * 0.95) - 1].Duration!.Value), MessageCatalog.Get("panels.n_rounds", Language, ("n", Format.Count(rounds.Length, Language)))));
        summary.Children.Add(Metric("panels.tool_time", Seconds(tools.Sum(r => r.Duration ?? 0)), MessageCatalog.Get("panels.n_calls", Language, ("n", Format.Count(tools.Length, Language)))));
        summary.Children.Add(Metric("panels.parallel_saving", Seconds(Math.Max(0, total - timeline.Span)), MessageCatalog.Get("panels.activity_time_minus_wall_time", Language)));
        summary.Children.Add(Metric("common.retry", Format.Count(retries, Language), MessageCatalog.Get(retries == 0 ? "panels.no_anomaly" : "panels.needs_attention", Language)));
        summary.Children.Add(Metric("panels.first_activity", Seconds(activities.Select(r => r.Start).DefaultIfEmpty().Min()), MessageCatalog.Get("panels.from_turn_start", Language)));
        var metricCard = AnalysisCard(summary, "class:run-kpis", new Thickness(0));
        void FitMetrics(double width)
        {
            summary.Columns = width <= 560 ? 2 : width <= 880 ? 3 : 6;
            metricCard.Height = 1 + (540 / summary.Columns);
        }

        FitMetrics(LayoutRoot.Bounds.Width);
        summary.SizeChanged += (_, _) => FitMetrics(LayoutRoot.Bounds.Width);
        body.Add(metricCard);

        var distribution = new StackPanel { Spacing = 9 };
        distribution.Children.Add(Detail(MessageCatalog.Get("panels.time_distribution", Language) + "   ·   " + MessageCatalog.Get("panels.time_cumulative_activity", Language, ("time", Seconds(total)))));
        var bar = new Grid { Height = 30 };
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        foreach (var (bucket, label, brush) in new[] { ("model", "common.models", "TextBrush"), ("tool", "common.tool", "BrandBrush"), ("recovery", "common.retry", "ErrBrush"), ("system", "common.system", "MutedBrush") })
        {
            var caption = MessageCatalog.Get(label, Language);
            var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center };
            dot.Bind(Border.BackgroundProperty, dot.GetResourceObservable(brush));
            var key = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            key.Children.Add(dot);
            key.Children.Add(Detail(caption));
            legend.Children.Add(key);
            var ms = activities.Where(r => r.Bucket == bucket).Sum(r => r.Duration ?? 0);
            if (ms > 0)
            {
                bar.ColumnDefinitions.Add(new ColumnDefinition(ms, GridUnitType.Star));
                var slice = new Border { CornerRadius = new CornerRadius(4) };
                slice.Bind(Border.BackgroundProperty, slice.GetResourceObservable(brush));
                slice.Child = new TextBlock { Text = caption + "  " + Seconds(ms), [!TextBlock.FontSizeProperty] = StudioTypography.Size(12), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                slice.Child.Bind(TextBlock.ForegroundProperty, slice.GetResourceObservable("RaisedBrush"));
                Place(bar, slice, bar.ColumnDefinitions.Count - 1);
            }
        }

        distribution.Children.Add(bar);
        distribution.Children.Add(legend);
        body.Add(AnalysisCard(distribution, "class:run-budget", new Thickness(15, 12)));

        var zoom = new Slider { Minimum = 1, Maximum = 16, Value = 1, Width = 130, TickFrequency = 1, IsSnapToTickEnabled = true };
        var scale = Detail("1×");
        var reset = new Button { Content = MessageCatalog.Get("panels.reset_zoom", Language), Height = 28, [!TextBlock.FontSizeProperty] = StudioTypography.Size(12) };
        AutomationProperties.SetAutomationId(zoom, "action:analysis.zoom");
        AutomationProperties.SetAutomationId(reset, "action:analysis.reset-zoom");
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        controls.Children.Add(Detail(MessageCatalog.Get("panels.timeline_zoom", Language)));
        controls.Children.Add(zoom);
        controls.Children.Add(scale);
        controls.Children.Add(reset);
        body.Add(controls);

        var track = new StackPanel { Spacing = 10 };
        var scroller = new ScrollViewer { Content = track, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var detail = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false, [!TextBlock.FontFamilyProperty] = StudioTypography.Resource("StudioMonoFamily"), [!TextBlock.FontSizeProperty] = StudioTypography.Size(12) };
        var signals = new Workbench.SignalRail();
        var points = new System.Collections.Generic.List<RunTimeline.Entry>();
        reset.Click += (_, _) =>
        {
            zoom.Value = 1;
            scroller.Offset = default;
        };
        var rows = new StackPanel { Spacing = 4 };
        foreach (var entry in timeline.Rows)
        {
            if (entry.Duration is not > 0 && entry.Label is "turn_done" or "usage" or "message")
            {
                continue;
            }

            var pick = new Button { Content = entry.Label, [!TextBlock.FontSizeProperty] = StudioTypography.Size(12), Margin = new Thickness(0, 0, 4, 2) };
            pick.Click += (_, _) => Inspect(entry);
            if (entry.Duration is not > 0)
            {
                points.Add(entry);
                continue;
            }

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
            row.Children.Add(pick);
            var lane = new Grid { Height = 8, VerticalAlignment = VerticalAlignment.Center };
            var before = Math.Max(0, entry.Start);
            var duration = entry.Duration.Value;
            lane.ColumnDefinitions.Add(new ColumnDefinition(before, GridUnitType.Star));
            lane.ColumnDefinitions.Add(new ColumnDefinition(duration, GridUnitType.Star));
            lane.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(0, timeline.Span - before - duration), GridUnitType.Star));
            var fill = new Border { CornerRadius = new CornerRadius(4) };
            fill.Bind(Border.BackgroundProperty, fill.GetResourceObservable("MutedBrush"));
            Place(lane, fill, 1);
            Place(row, lane, 1);
            Place(row, Detail(Seconds(duration)), 2);
            rows.Children.Add(row);
        }

        signals.Open(points, timeline.Span, Inspect);
        var signalCard = new StackPanel { Spacing = 8 };
        signalCard.Children.Add(Detail(MessageCatalog.Get("panels.signal_rail", Language)));
        signalCard.Children.Add(signals);
        track.Children.Add(AnalysisCard(signalCard, "class:run-signals", new Thickness(15)));
        var roundsCard = new StackPanel { Spacing = 8 };
        roundsCard.Children.Add(Detail(MessageCatalog.Get("panels.activity_rounds", Language)));
        roundsCard.Children.Add(rows);
        track.Children.Add(AnalysisCard(roundsCard, "class:run-rounds", new Thickness(15)));
        scroller.SizeChanged += (_, e) => track.Width = e.NewSize.Width * zoom.Value;
        zoom.ValueChanged += (_, _) =>
        {
            track.Width = scroller.Bounds.Width * zoom.Value;
            scale.Text = zoom.Value.ToString("0", CultureInfo.InvariantCulture) + "×";
        };
        body.Add(scroller);
        body.Add(detail);

        void Inspect(RunTimeline.Entry entry)
        {
            detail.Text = string.Join(Environment.NewLine, entry.Frames.Select(frame => frame.ToJsonString(new JsonSerializerOptions { WriteIndented = true })));
            detail.IsVisible = true;
        }
    }

    /// <summary>Builds a labelled metric from the trace.</summary>
    /// <param name="key">Metric label.</param>
    /// <param name="value">Formatted measurement.</param>
    /// <param name="note">Description or sample count.</param>
    /// <returns>The metric cell.</returns>
    private StackPanel Metric(string key, string value, string note)
    {
        var cell = new StackPanel { Spacing = 5, Margin = new Thickness(12), VerticalAlignment = VerticalAlignment.Center };
        cell.Children.Add(Detail(MessageCatalog.Get(key, Language)));
        cell.Children.Add(new SelectableTextBlock { Text = value, [!TextBlock.FontSizeProperty] = StudioTypography.Size(20), FontWeight = FontWeight.SemiBold, [!TextBlock.FontFamilyProperty] = StudioTypography.Resource("StudioMonoFamily") });
        cell.Children.Add(Detail(note));
        return cell;
    }
}
