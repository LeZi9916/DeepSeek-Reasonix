using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Reasonix.Studio.Shell.Metrics;

/// <summary>Measured context classes, capacity, instruction files and maintenance actions.</summary>
public sealed partial class ContextSummaryCard : UserControl
{
    /// <summary>The measured breakdown currently displayed.</summary>
    private ContextBreakdown? _context;

    /// <summary>The current interface language.</summary>
    private InterfaceLanguage _language;

    /// <summary>The one class whose largest holders are expanded.</summary>
    private string? _expanded;

    /// <summary>Initializes a new instance of the <see cref="ContextSummaryCard"/> class.</summary>
    public ContextSummaryCard()
    {
        InitializeComponent();
        Manage.Click += (_, _) => ManageRequested?.Invoke();
        Compact.Click += (_, _) => CompactRequested?.Invoke();
        Segments.SizeChanged += (_, _) => DrawSegments();
        AttachedToVisualTree += (_, _) =>
        {
            DrawParts();
            DrawSegments();
        };
        ActualThemeVariantChanged += (_, _) =>
        {
            DrawParts();
            DrawSegments();
        };
    }

    /// <summary>Raised to enter the existing MCP settings section.</summary>
    public event Action? ManageRequested;

    /// <summary>Raised to ask for explicit compaction confirmation.</summary>
    public event Action? CompactRequested;

    /// <summary>Projects measured context and the current MCP connection inventory.</summary>
    /// <param name="context">Host measurements.</param><param name="servers">Canonical MCP entries.</param>
    /// <param name="busy">Whether maintenance is blocked.</param><param name="pending">Whether compression awaits a typed outcome.</param>
    /// <param name="language">Interface language.</param>
    public void Show(ContextBreakdown? context, JsonArray? servers, bool busy, bool pending, InterfaceLanguage language)
    {
        _context = context;
        _language = language;
        Title.Text = Say("common.context_details");
        Subtitle.Text = Say("composer.content_sent_to_the_model");
        Percent.Text = context is { Window: > 0 } ? Math.Min(100, Math.Round(100d * context.Used / context.Window, MidpointRounding.AwayFromZero)).ToString(System.Globalization.CultureInfo.CurrentCulture) + "%" : "—";
        Used.Text = Say("common.used") + " " + MetricReadouts.Tokens(context?.Used ?? 0);
        Capacity.Text = Say("common.model_capacity") + " " + MetricReadouts.Tokens(context?.Window ?? 0);
        CompactThreshold.IsVisible = context?.CompactAt > 0;
        CompactThreshold.Text = "· " + MessageCatalog.Get("composer.auto_compacts_at", language, ("n", MetricReadouts.Tokens(context?.CompactAt ?? 0)));
        Manage.Content = Say("composer.manage_mcp");
        Compact.Content = Say(pending ? "common.compacting" : "composer.compact_now");
        Compact.IsEnabled = context is not null && !busy && !pending;
        Used.IsVisible = Capacity.IsVisible = context is not null;
        if (Segments.Parent is Border capacityBar)
        {
            capacityBar.IsVisible = context is not null;
        }

        var active = servers?.OfType<JsonObject>().Where(server => (bool?)server["enabled"] == true).ToArray() ?? [];
        var ready = active.Where(server => (string?)server["state"] == "ready").ToArray();
        Mcp.Text = "MCP · " + ready.Length.ToString(System.Globalization.CultureInfo.CurrentCulture) + " " + Say("composer.connected") + " · " + ready.Sum(server => (int?)server["tools"] ?? 0).ToString(System.Globalization.CultureInfo.CurrentCulture) + " " + Say("composer.tools");
        ToolTip.SetTip(Mcp, string.Join("\n", active.Select(server => (string?)server["name"] + " · " + ((string?)server["state"] == "ready" ? ((int?)server["tools"] ?? 0).ToString(System.Globalization.CultureInfo.CurrentCulture) + " " + Say("composer.tools") : Say("composer.waiting_to_connect")))));
        DrawParts();
        DrawSegments();
    }

    /// <summary>Enumerates only classes for which the host measured a contribution.</summary>
    /// <returns>The visible classes.</returns>
    private IEnumerable<(string Key, string Label, long Tokens, string Brush, double Opacity)> PartsOf()
    {
        if (_context is not { } context)
        {
            yield break;
        }

        foreach (var (key, label, tokens, brush, opacity) in new[]
        {
            ("sys", "common.system_prompt", context.System, "MutedBrush", 1d),
            ("tool", "common.tool_definitions", context.Tools, "NetBrush", 1d),
            ("memory", "panels.project_memory", context.ProjectMemory, "Cat6Brush", 1d),
            ("user", "common.what_you_said", context.User, "NetBrush", 0.6),
            ("reply", "common.model_replies", context.Reply, "NetBrush", 0.35),
            ("digest", "panels.folded_history", context.Digest, "NetBrush", 0.35),
            ("output", "common.tool_output", context.Output, "NetBrush", 0.2),
            ("idle", "panels.idle", Math.Max(0, (context.CompactAt > 0 && context.CompactAt <= context.Window ? context.CompactAt : context.Window) - context.Used), "OverlayBrush", 1d),
        })
        {
            if (tokens > 0)
            {
                yield return (key, Say(label), tokens, brush, opacity);
            }
        }
    }

    /// <summary>Builds accessible class disclosures without interpreting message content.</summary>
    private void DrawParts()
    {
        Parts.Children.Clear();
        Details.Children.Clear();
        Hint.Text = Say(_context is null ? "composer.no_context_data_yet" : "composer.open_a_class_for_its_largest");
        foreach (var part in PartsOf())
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("8,*,Auto"), ColumnSpacing = 8, Height = 24 };
            var swatch = new Border { Width = 8, Height = 8, [!Border.BackgroundProperty] = new DynamicResourceExtension(part.Brush), Opacity = part.Opacity, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center };
            if (part.Key == "idle")
            {
                swatch.Background = Brushes.Transparent;
                swatch[!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderBrush");
                swatch.BorderThickness = new Thickness(1);
            }

            row.Children.Add(swatch);
            var label = new TextBlock { Text = part.Label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedBrush") };
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            var canOpen = part.Key == "memory" ? _context!.ProjectMemoryFiles.Length > 0 : _context!.Largest.Any(holder => holder.Class == part.Key);
            if (!canOpen)
            {
                var text = new TextBlock { Text = MetricReadouts.Tokens(part.Tokens), FontSize = 12, [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("MutedBrush"), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(text, 2);
                row.Children.Add(text);
                Parts.Children.Add(row);
                continue;
            }

            var count = new Button { Content = MetricReadouts.Tokens(part.Tokens) + " ›", Classes = { "context-disclose" }, Height = 24, FontSize = 12 };
            count[!Button.ForegroundProperty] = new DynamicResourceExtension("TextBrush");
            AutomationProperties.SetAutomationId(count, part.Key == "memory" ? "action:context.memory-files" : "action:context.largest");
            AutomationProperties.SetName(count, part.Label);
            count.Click += (_, _) =>
            {
                _expanded = _expanded == part.Key ? null : part.Key;
                DrawParts();
            };
            Grid.SetColumn(count, 2);
            row.Children.Add(count);
            Parts.Children.Add(row);
            if (_expanded != part.Key)
            {
                continue;
            }

            if (part.Key == "memory")
            {
                foreach (var file in _context!.ProjectMemoryFiles)
                {
                    AddDetail(file.Path, file.Scope, file.Tokens);
                }
            }
            else
            {
                foreach (var holder in _context!.Largest.Where(holder => holder.Class == part.Key))
                {
                    AddDetail(holder.Tool ?? part.Label, holder.Active ? Say("composer.running_turn") : string.Empty, holder.Tokens);
                }
            }
        }
    }

    /// <summary>Displays one measured holder or instruction file.</summary>
    /// <param name="label">Canonical name.</param><param name="scope">Scope or running marker.</param><param name="tokens">Measured size.</param>
    private void AddDetail(string label, string scope, long tokens)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 7 };
        row.Children.Add(new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11 });
        var note = new TextBlock { Text = scope, [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("GhostBrush"), FontSize = 10 };
        Grid.SetColumn(note, 1);
        row.Children.Add(note);
        var size = new TextBlock { Text = MetricReadouts.Tokens(tokens), FontSize = 11 };
        Grid.SetColumn(size, 2);
        row.Children.Add(size);
        ToolTip.SetTip(row, label);
        Details.Children.Add(row);
    }

    /// <summary>Scales each class to the fold threshold or model capacity.</summary>
    private void DrawSegments()
    {
        Segments.Children.Clear();
        if (_context is not { Window: > 0 } context)
        {
            return;
        }

        var ceiling = context.CompactAt > 0 && context.CompactAt <= context.Window ? context.CompactAt : context.Window;
        var left = 0d;
        foreach (var part in PartsOf())
        {
            var width = Math.Max(0.01, Math.Min(1, (double)part.Tokens / ceiling)) * Segments.Bounds.Width;
            var segment = new Border { Width = width, Height = 8, [!Border.BackgroundProperty] = new DynamicResourceExtension(part.Brush), Opacity = part.Opacity };
            Canvas.SetLeft(segment, left);
            left += width;
            ToolTip.SetTip(segment, part.Label + " " + MetricReadouts.Tokens(part.Tokens));
            Segments.Children.Add(segment);
        }
    }

    /// <summary>Resolves a label in the card's language.</summary>
    /// <param name="key">Message identity.</param><returns>The localized label.</returns>
    private string Say(string key) => MessageCatalog.Get(key, _language);
}
