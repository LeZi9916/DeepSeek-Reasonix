using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
using Path = Avalonia.Controls.Shapes.Path;

namespace Reasonix.Studio.Shell.Composer;

public sealed partial class ModelPicker
{
    /// <summary>Builds one account heading.</summary>
    /// <param name="account">The account to name.</param>
    /// <param name="divided">Whether a previous heading or row precedes it.</param>
    /// <returns>The heading.</returns>
    private Control GroupHead(Account account, bool divided)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*,Auto"), ColumnSpacing = 8 };
        grid.Children.Add(MarkControl(account.Mark, account.Label, 20));
        var name = new TextBlock { Text = account.Label, VerticalAlignment = VerticalAlignment.Center };
        name.Classes.Add("model-group-name");
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        if (!string.IsNullOrWhiteSpace(account.Host))
        {
            var host = new TextBlock { Text = account.Host, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 190 };
            host.Classes.Add("model-group-host");
            Grid.SetColumn(host, 2);
            grid.Children.Add(host);
        }

        var border = new Border { Child = grid };
        border.Classes.Add("model-group");
        border.Classes.Set("divided", divided);
        return border;
    }

    /// <summary>Builds one model row.</summary>
    /// <param name="model">The model to draw.</param>
    /// <param name="current">Whether it is the session's model.</param>
    /// <returns>The row.</returns>
    private Button ModelRow(ModelChoice model, bool current)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("18,*,Auto"), ColumnSpacing = 8 };
        grid.Children.Add(MarkControl(MarkOf(model), Label(model), 18));
        var text = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var name = new TextBlock { Text = Label(model) };
        name.Classes.Add("model-name");
        text.Children.Add(name);
        var description = Describe(model);
        if (description.Length > 0)
        {
            var kind = new TextBlock { Text = description };
            kind.Classes.Add("model-kind");
            text.Children.Add(kind);
        }

        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var tail = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (HealthBadge(model.Provider) is { } health)
        {
            tail.Children.Add(health);
        }

        if (model.Vision)
        {
            tail.Children.Add(Badge(MessageCatalog.Get("common.reads_images", _language)));
        }

        if (current)
        {
            tail.Children.Add(Check());
        }

        Grid.SetColumn(tail, 2);
        grid.Children.Add(tail);
        var button = new Button { Content = grid, Tag = model.Ref, IsEnabled = !model.NotAdded };
        button.Classes.Add("model-row");
        button.Classes.Set("current", current);
        AutomationProperties.SetName(button, Label(model));
        ToolTip.SetTip(button, model.Ref);
        button.Click += (_, _) => Choose(model.Ref);
        return button;
    }

    /// <summary>Builds one effort row.</summary>
    /// <param name="level">Kernel effort value.</param>
    /// <param name="current">The configured effort.</param>
    /// <param name="forced">Whether thinking cannot be switched off.</param>
    /// <param name="cheapest">The lowest real level.</param>
    /// <returns>The row.</returns>
    private Button EffortRow(string level, string current, bool forced, string cheapest)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,30,14"), ColumnSpacing = 7 };
        var text = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var name = new TextBlock { Text = EffortLabel(level) };
        name.Classes.Add("effort-name");
        line.Children.Add(name);
        var api = new TextBlock { Text = EffortApi(level), VerticalAlignment = VerticalAlignment.Center };
        api.Classes.Add("effort-api");
        line.Children.Add(api);
        if (level.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            line.Children.Add(Badge(MessageCatalog.Get("composer.recommended", _language)));
        }

        text.Children.Add(line);
        var description = EffortDescription(level, forced, cheapest);
        var note = new TextBlock { Text = description };
        note.Classes.Add("effort-desc");
        text.Children.Add(note);
        grid.Children.Add(text);
        var strength = Strength(level);
        Grid.SetColumn(strength, 1);
        grid.Children.Add(strength);
        if (level.Equals(current, StringComparison.OrdinalIgnoreCase))
        {
            grid.Children.Add(CheckAt(2));
        }

        var button = new Button { Content = grid, Tag = level };
        button.Classes.Add("effort-row");
        button.Classes.Set("current", level.Equals(current, StringComparison.OrdinalIgnoreCase));
        AutomationProperties.SetName(button, EffortLabel(level));
        ToolTip.SetTip(button, description);
        button.Click += (_, _) => ChooseEffort(level);
        return button;
    }

    /// <summary>Builds a model-mode toggle row.</summary>
    /// <param name="mode">The mode to draw.</param>
    /// <param name="divided">Whether the mode section starts here.</param>
    /// <returns>The row.</returns>
    private Button ModeRow(ModelModeChoice mode, bool divided)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        var text = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var name = new TextBlock { Text = ModeLabel(mode) };
        name.Classes.Add("effort-name");
        line.Children.Add(name);
        if (mode.Costlier)
        {
            line.Children.Add(Badge(MessageCatalog.Get("composer.costs_more", _language)));
        }

        text.Children.Add(line);
        var hint = ModeHint(mode);
        if (hint.Length > 0)
        {
            var note = new TextBlock { Text = hint };
            note.Classes.Add("effort-desc");
            text.Children.Add(note);
        }

        grid.Children.Add(text);
        var track = new Border();
        track.Classes.Add("mode-switch");
        track.Classes.Set("on", mode.Active);
        var knob = new Ellipse();
        knob.Classes.Add("mode-knob");
        track.Child = knob;
        Grid.SetColumn(track, 1);
        grid.Children.Add(track);
        var button = new Button { Content = grid, Tag = mode.Id };
        button.Classes.Add("effort-row");
        button.Classes.Set("divided", divided);
        AutomationProperties.SetName(button, ModeLabel(mode));
        button.Click += (_, _) =>
        {
            Close();
            ModePicked?.Invoke(mode.Active ? string.Empty : mode.Id);
        };
        return button;
    }

    /// <summary>Builds the route offered when the endpoint declared no levels.</summary>
    /// <returns>The declaration row.</returns>
    private Button DeclareRow()
    {
        var text = new StackPanel { Orientation = Orientation.Vertical };
        var name = new TextBlock { Text = MessageCatalog.Get("composer.declare_reasoning_levels", _language) };
        name.Classes.Add("effort-name");
        text.Children.Add(name);
        var note = new TextBlock { Text = MessageCatalog.Get("composer.this_endpoint_reported_no_reasoning", _language) };
        note.Classes.Add("effort-desc");
        text.Children.Add(note);
        var button = new Button { Content = text };
        button.Classes.Add("effort-row");
        button.Click += (_, _) =>
        {
            Close();
            ManageRequested?.Invoke();
        };
        return button;
    }

    /// <summary>Draws the strength bars, or the recommendation mark for Auto.</summary>
    /// <param name="level">Kernel effort value.</param>
    /// <returns>The indicator.</returns>
    private Control Strength(string level)
    {
        var id = level.ToLowerInvariant();
        if (id == "auto")
        {
            return new PathIcon
            {
                Data = Geometry("Glyph.fresh"),
                Width = 13,
                Height = 13,
                Foreground = Brush("FaintBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
        }

        var value = id switch
        {
            "disabled" or "none" => 0,
            "low" => 1,
            "medium" => 2,
            "high" => 3,
            "xhigh" or "max" => 4,
            _ => 1,
        };
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("4,4,4,4"),
            Width = 18,
            Height = 14,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        for (var i = 1; i <= 4; i++)
        {
            var bar = new Border { Height = i switch { 1 => 4, 2 => 7, 3 => 10, _ => 13 } };
            bar.Classes.Add("effort-bar");
            bar.Classes.Set("on", i <= value);
            Grid.SetColumn(bar, i - 1);
            grid.Children.Add(bar);
        }

        return grid;
    }

    /// <summary>Builds the check mark that names the chosen row.</summary>
    /// <returns>The mark.</returns>
    private Control Check() => CheckAt(0);

    /// <summary>Builds a check mark in a grid column.</summary>
    /// <param name="column">Column, or zero outside a grid.</param>
    /// <returns>The mark.</returns>
    private Control CheckAt(int column)
    {
        var path = new Path
        {
            Data = Geometry("Glyph.check"),
            Stroke = Brush("TextBrush"),
            StrokeThickness = 1.8,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Stretch = Stretch.Uniform,
            Width = 13,
            Height = 13,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        if (column > 0)
        {
            Grid.SetColumn(path, column);
        }

        return path;
    }

    /// <summary>Builds a vendor letter chip.</summary>
    /// <param name="mark">Vendor slug, or empty for a custom account.</param>
    /// <param name="label">Name the chip abbreviates.</param>
    /// <param name="size">Box size.</param>
    /// <returns>The chip.</returns>
    private Control MarkControl(string mark, string label, double size)
    {
        var markGeometry = Geometry("Compose.deepseek");
        if (mark.Equals("deepseek", StringComparison.OrdinalIgnoreCase) && markGeometry.Bounds.Width > 0)
        {
            var logo = new Canvas { Width = 24, Height = 24 };
            logo.Children.Add(new Path
            {
                Data = markGeometry,
                Fill = new SolidColorBrush(Color.Parse("#4D6BFE")),
            });
            return new Viewbox { Width = size, Height = size, Child = logo, VerticalAlignment = VerticalAlignment.Center };
        }

        var tone = Tone(mark.Length == 0 ? label : mark);
        var text = new TextBlock
        {
            Text = label.Trim().Length == 0 ? "?" : label.Trim()[..1].ToUpperInvariant(),
            Foreground = new SolidColorBrush(tone),
            Width = size,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.Classes.Add("model-mark-text");
        text.FontSize = size >= 20 ? 10 : 9;
        var chip = new Border { Width = size, Height = size, Child = text };
        chip.Classes.Add("model-mark");
        chip.Background = new SolidColorBrush(tone, 0.16);
        chip.CornerRadius = new CornerRadius(size >= 20 ? 6 : 5);
        return chip;
    }

    /// <summary>Builds the provider health reading for a model row.</summary>
    /// <param name="provider">Configuration entry name.</param>
    /// <returns>The badge, or null when checks are off or the reading is silent.</returns>
    private Control? HealthBadge(string provider)
    {
        if (!_health.TryGetValue(provider, out var health) || health.Connection.Length == 0 || health.Connection == "disabled")
        {
            return null;
        }

        var (key, tone) = health.Connection switch
        {
            "reachable" when health.Wallet == "insufficient" => ("common.insufficient_balance", "warning"),
            "reachable" when health.Wallet == "auth" => ("models.balance_check_failed", "warning"),
            "reachable" => ("models.connected", "positive"),
            "auth" => ("models.auth_failed", "warning"),
            "missing_key" => ("models.missing_key", "warning"),
            "payment_required" => ("models.payment_required", "warning"),
            "quota_exhausted" => ("models.quota_exhausted", "warning"),
            "rate_limited" => ("models.rate_limited", "warning"),
            "timeout" => ("models.timed_out", "warning"),
            "unreachable" => ("models.connection_failed", "warning"),
            "redirect_refused" => ("models.redirect_refused", "warning"),
            _ => ("models.unconfirmed", "neutral"),
        };
        var badge = (Border)Badge(MessageCatalog.Get(key, _language));
        badge.Classes.Set("warning", tone == "warning");
        badge.Classes.Set("positive", tone == "positive");
        return badge;
    }

    /// <summary>Builds a small muted badge.</summary>
    /// <param name="text">Badge text.</param>
    /// <returns>The badge.</returns>
    private Control Badge(string text)
    {
        var label = new TextBlock { Text = text };
        label.Classes.Add("model-badge-text");
        var badge = new Border { Child = label };
        badge.Classes.Add("model-badge");
        return badge;
    }
}
