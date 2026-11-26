using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Shell.Host;
using Reasonix.Studio.Shell.Settings;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Formats byte totals using the same binary units as the storage view.</summary>
    /// <param name="value">Value to format.</param>
    /// <returns>Formatted byte count.</returns>
    private static string DataBytes(double value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return value.ToString("0.#", CultureInfo.CurrentCulture) + " " + units[index];
    }

    /// <summary>Formats a usage counter without losing its unit.</summary>
    /// <param name="value">Value to format.</param>
    /// <returns>Formatted count.</returns>
    private static string DataCount(double value) => value >= 1000000 ? (value / 1000000).ToString("0.#", CultureInfo.CurrentCulture) + "M" : value >= 1000 ? (value / 1000).ToString("0.#", CultureInfo.CurrentCulture) + "k" : value.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Reads a numeric JSON property.</summary>
    /// <param name="row">Source record.</param>
    /// <param name="key">Resource or field name.</param>
    /// <returns>The numeric value, or zero for absent counts.</returns>
    private static double Quantity(JsonNode? row, string key) => (double?)row?[key] ?? 0;

    /// <summary>Formats each billed currency separately.</summary>
    /// <param name="items">Reported amounts by currency.</param>
    /// <returns>Formatted currency amounts.</returns>
    private static string Money(JsonNode? items)
    {
        if (items is not JsonArray { Count: > 0 } values)
        {
            return "—";
        }

        return string.Join(" / ", values.Select(m =>
        {
            var code = (string?)m?["currency"];
            var amount = decimal.TryParse(m?["amount"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
            var symbol = code switch
            {
                "CNY" => "¥",
                "USD" => "$",
                _ => code + " ",
            };
            return symbol + amount.ToString("0.00", CultureInfo.CurrentCulture);
        }));
    }

    /// <summary>Exports one daily row and one column per reported currency.</summary>
    /// <param name="report">The displayed report.</param>
    /// <returns>The CSV contents.</returns>
    private static string UsageCsv(JsonObject report)
    {
        var days = report["daily"]?.AsArray().OfType<JsonObject>().ToArray() ?? [];
        var currencies = days.SelectMany(d => (d["cost"]?.AsArray() ?? []).Select(m => (string?)m?["currency"])).OfType<string>().Distinct().ToArray();
        var rows = days.Select(d => new[] { "day", "total", "cacheHit", "cacheMiss", "requests" }.Select(k => d[k]?.ToString() ?? string.Empty)
            .Concat(currencies.Select(c => (d["cost"]?.AsArray() ?? []).FirstOrDefault(m => (string?)m?["currency"] == c)?["amount"]?.ToString() ?? string.Empty)));
        var header = new[] { "day", "provider_total_tokens", "input_cache_hit", "input_cache_miss", "requests" }.Concat(currencies.Select(c => "cost_" + c));
        return string.Join("\n", rows.Prepend(header).Select(row => string.Join(",", row.Select(Cell)))) + "\n";

        static string Cell(string value) => value.IndexOfAny([',', '"', '\r', '\n']) < 0 ? value : "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>Builds the machine's usage report, upload totals and currency selector.</summary>
    /// <param name="card">Parent settings card.</param>
    /// <param name="api">Active kernel client.</param>
    /// <returns>The completed operation.</returns>
    private async Task AddUsageAsync(StackPanel card, StudioPageClient api)
    {
        var token = _closing.Token;
        var group = DataGroup(card, "usage.usage_cost", "settings.usage_hint");
        var content = new StackPanel { Spacing = 14, Margin = new Thickness(0, 2, 0, 12) };
        group.Children.Add(content);
        var providers = await api.ProvidersAsync(token).ConfigureAwait(true);
        var days = 30;
        string? from = null;
        string? to = null;
        long generation = 0;
        var choices = new WrapPanel();
        var dates = DataText(string.Empty, 12, 17);
        dates.VerticalAlignment = VerticalAlignment.Center;
        var export = DataButton(DataLabel("usage.export_csv"));
        AutomationProperties.SetAutomationId(export, "action:usage.export");
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        header.Children.Add(choices);
        DataColumn(header, 1, dates);
        DataColumn(header, 2, export);
        export.Padding = new Thickness(12, 0);
        header.SizeChanged += (_, e) =>
        {
            var small = e.NewSize.Width < choices.DesiredSize.Width + dates.DesiredSize.Width + export.DesiredSize.Width + 24;
            Grid.SetColumnSpan(choices, small ? 3 : 1);
            Grid.SetRow(dates, small ? 1 : 0);
            Grid.SetColumn(dates, small ? 0 : 1);
            Grid.SetRow(export, small ? 1 : 0);
            dates.Margin = small ? default : new Thickness(12, 0);
            export.Margin = small ? new Thickness(0, 12, 0, 0) : default;
            dates.HorizontalAlignment = HorizontalAlignment.Left;
        };
        content.Children.Add(header);
        var form = new WrapPanel { IsVisible = false };
        var start = new CalendarDatePicker();
        var end = new CalendarDatePicker();
        form.Children.Add(start);
        form.Children.Add(end);
        var apply = DataButton(DataLabel("usage.apply"));
        form.Children.Add(apply);
        content.Children.Add(form);
        var panels = new StackPanel { Spacing = 14 };
        content.Children.Add(panels);
        JsonObject? displayed = null;
        var notice = DataText(string.Empty, 12, 18, true);
        notice.IsVisible = false;
        content.Children.Add(notice);
        foreach (var (key, n) in new[] { ("usage.today", 1), ("usage.n_7_days", 7), ("usage.n_30_days", 30), ("common.all", 365), ("common.custom", -1), ("usage.calendar_month", -2) })
        {
            var button = DataButton(DataLabel(key), 29);
            button.Margin = new Thickness(1, 2);
            choices.Children.Add(button);
            button.Click += async (_, _) =>
            {
                form.IsVisible = n < 0;
                end.IsVisible = n != -2;
                if (n < 0)
                {
                    start.SelectedDate ??= DateTime.Today;
                    end.SelectedDate ??= DateTime.Today;
                    return;
                }

                days = n;
                from = to = null;
                await Reload().ConfigureAwait(true);
            };
        }

        apply.Click += async (_, _) =>
        {
            if (start.SelectedDate is not DateTime first)
            {
                return;
            }

            var last = end.IsVisible ? end.SelectedDate ?? first : new DateTime(first.Year, first.Month, 1).AddMonths(1).AddDays(-1);
            first = end.IsVisible ? first : new DateTime(first.Year, first.Month, 1);
            if (first <= last)
            {
                from = first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                to = last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                await Reload().ConfigureAwait(true);
            }
        };
        export.Click += async (_, _) =>
        {
            if (displayed is null)
            {
                return;
            }

            try
            {
                var csv = UsageCsv(displayed);
                await new AvaloniaHost(this).SaveTextAsync("reasonix-usage-" + (string?)displayed["from"] + "-" + (string?)displayed["to"] + ".csv", csv).ConfigureAwait(true);
            }
            catch (Exception error)
            {
                notice.Text = DescribeError(error);
                notice.IsVisible = true;
            }
        };
        await Reload().ConfigureAwait(true);
        var upload = await api.UploadsAsync(token).ConfigureAwait(true);
        var traffic = DataGroup(card, "usage.upload_volume", "settings.size_of_requests_sent");
        var info = DataText(DataLabel("usage.the_size_of_the_request"), 13, 21.4375, true);
        info.HorizontalAlignment = HorizontalAlignment.Left;
        info.MaxWidth = 520.27;
        info.Margin = new Thickness(0, 11, 0, 0);
        traffic.Children.Add(info);
        var total = Quantity(upload, "requests");
        var uploadProviders = upload["providers"]?.AsArray() ?? [];
        if (total == 0)
        {
            traffic.Children.Add(DataText(DataLabel("usage.nothing_recorded_yet_counting_starts"), 12, 19.59375, true));
        }
        else
        {
            Traffic(DataLabel("usage.uploaded_today"), DataBytes(Quantity(upload["daily"]?.AsArray().LastOrDefault(), "bytes")));
            Traffic(DataLabel("usage.uploaded_in_the_last_n", ("n", "30")), DataLabel("usage.size_in_n_requests_avg", ("size", DataBytes(Quantity(upload, "bytes"))), ("n", DataCount(total)), ("avg", DataBytes(Quantity(upload, "bytes") / total))));
            foreach (var p in uploadProviders)
            {
                Traffic((string?)p?["provider"] ?? "—", DataBytes(Quantity(p, "bytes")) + " (" + Quantity(p, "percent").ToString("0.#", CultureInfo.CurrentCulture) + "%)");
            }

            traffic.Children.Add(new Border { Height = 1 });
        }

        var setting = await api.CurrencyAsync(null, token).ConfigureAwait(true);
        var modes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var frame = DataFrame(modes, new Thickness(2));
        frame.BorderThickness = default;
        DataRow(card, "usage.cost_display_currency", "settings.which_currency_costs_show", frame);
        foreach (var (id, key) in new[] { ("auto", "common.auto"), ("CNY", "usage.chinese_yuan_cny"), ("USD", "usage.us_dollar_usd") })
        {
            var pick = new RadioButton { Content = DataLabel(key), Tag = id, IsChecked = (string?)setting["mode"] == id, Margin = default };
            pick.Classes.Add("segmented");
            pick.Classes.Add("data-choice");
            AutomationProperties.SetAutomationId(pick, "action:currency.mode");
            modes.Children.Add(pick);
            pick.Click += async (_, _) =>
            {
                modes.IsEnabled = false;
                try
                {
                    setting = await api.CurrencyAsync(id, token).ConfigureAwait(true);
                    await Reload().ConfigureAwait(true);
                }
                catch (Exception error)
                {
                    notice.Text = DescribeError(error);
                    notice.IsVisible = true;
                }
                finally
                {
                    foreach (var option in modes.Children.OfType<RadioButton>())
                    {
                        option.IsChecked = Equals(option.Tag, (string?)setting["mode"]);
                    }

                    modes.IsEnabled = true;
                }
            };
        }

        void Traffic(string key, string value)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(DataText(key, 12, 19.59375));
            DataColumn(row, 1, DataText(value, 12, 19.59375));
            traffic.Children.Add(row);
        }

        async Task Reload()
        {
            var ticket = ++generation;
            try
            {
                var report = await api.UsageAsync(days, from, to, token).ConfigureAwait(true);
                if (ticket != generation)
                {
                    return;
                }

                displayed = report;
                dates.Text = (string?)report["from"] + " → " + (string?)report["to"];
                panels.Children.Clear();
                UsageSummary(panels, report, providers);
                notice.IsVisible = false;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                if (ticket == generation)
                {
                    notice.Text = DescribeError(error);
                    notice.IsVisible = true;
                }
            }
        }
    }

    /// <summary>Draws report cards, independently scaled charts, daily rows and allocation bars.</summary>
    /// <param name="panel">Parent panel.</param>
    /// <param name="r">Current usage report.</param>
    /// <param name="providers">Provider labels for the recorded source IDs.</param>
    private void UsageSummary(StackPanel panel, JsonObject r, JsonArray providers)
    {
        var daily = r["daily"]?.AsArray().OfType<JsonObject>().ToArray() ?? [];
        if (Quantity(r, "requests") == 0 && Quantity(r, "tokens") == 0)
        {
            panel.Children.Add(DataText(DataLabel("usage.nothing_recorded_yet_counting_starts"), 13, 19, true));
            return;
        }

        var priced = daily.Where(d => d["cost"] is JsonArray { Count: > 0 }).ToArray();
        var hit = Quantity(r, "cache_hit");
        var input = hit + Quantity(r, "cache_miss");
        var tiles = new DataColumns(12, 560);
        panel.Children.Add(tiles);
        var paid = Tile(DataLabel((bool?)r["costEstimated"] == true || (bool?)r["costIncomplete"] == true ? "usage.spend_2" : "usage.paid"), Money(r["cost"]), true);
        if (r["estimateCost"] is JsonArray { Count: > 0 })
        {
            Note(paid, DataLabel("usage.api_priced_estimate") + " " + Money(r["estimateCost"]) + " · " + DataLabel("usage.subscription_use_priced_at_api_2"));
        }

        if (priced.Length > 0)
        {
            Note(paid, DataLabel("usage.from_day_n_days_recorded", ("day", (string?)priced[0]["day"] ?? string.Empty), ("n", priced.Length.ToString(CultureInfo.CurrentCulture))));
        }

        Note(Tile(DataLabel("usage.input"), DataCount(input)), DataLabel("usage.cache_hit_rate_note", ("rate", (input == 0 ? 0 : hit * 100 / input).ToString("0.0", CultureInfo.CurrentCulture))));
        Note(Tile(DataLabel("usage.provider_reported_total"), DataCount(Quantity(r, "tokens"))), DataLabel("usage.total_includes_output"));
        var req = Tile(DataLabel("usage.requests"), DataCount(Quantity(r, "requests")));
        Note(req, DataLabel("usage.n_turns", ("n", DataCount(Quantity(r, "turns")))));
        Note(req, DataLabel("usage.of_n_days_avg", ("n", daily.Length.ToString(CultureInfo.CurrentCulture)), ("avg", DataCount(Quantity(r, "requests") / Math.Max(1, Quantity(r, "active_days"))))));
        var charts = new DataColumns(14, 700, 1.3, 1);
        panel.Children.Add(charts);
        var tokens = ChartPanel(charts, "usage.daily_usage");
        tokens.Children.Add(DataText(DataLabel("usage.input_cache_hit") + "   " + DataLabel("usage.input_cache_miss"), 12, 17));
        tokens.Children.Add(new UsagePlot(daily.Select(d => ((string?)d["day"] ?? string.Empty, Quantity(d, "cacheHit"), Quantity(d, "cacheMiss"))).ToArray()) { Margin = new Thickness(0, 6, 0, 0) });
        var spend = new StackPanel { Spacing = 14 };
        charts.Children.Add(spend);
        var currencies = priced.SelectMany(d => d["cost"]!.AsArray()).Select(c => (string?)c?["currency"]).OfType<string>().Distinct().ToArray();
        foreach (var code in currencies)
        {
            var chart = ChartPanel(spend, "usage.daily_spend", code);
            var hint = DataText(DataLabel("usage.drawn_apart_from_usage_different"), 13, 19, true);
            hint.Margin = new Thickness(0, -6, 0, 12);
            chart.Children.Add(hint);
            chart.Children.Add(new UsagePlot(priced.Where(d => d["cost"]!.AsArray().Any(c => (string?)c?["currency"] == code)).Select(d => ((string?)d["day"] ?? string.Empty, Amount(d, code), 0d)).ToArray()));
            var unpriced = daily.Where(d => Quantity(d, "total") > 0 && d["cost"] is not JsonArray { Count: > 0 } && d["estimateCost"] is not JsonArray { Count: > 0 }).ToArray();
            if (code == currencies.Last() && unpriced.Length > 0)
            {
                var lines = new StackPanel();
                lines.Children.Add(DataText(DataLabel("usage.n_more_days_have_usage", ("n", unpriced.Length.ToString(CultureInfo.CurrentCulture))), 13, 20.140625, true));
                chart.Children.Add(new Border { Padding = new Thickness(12, 10), Margin = new Thickness(0, 12, 0, 0), Child = lines });
            }
        }

        var table = new StackPanel();
        table.Children.Add(DataText(DataLabel("usage.daily_details"), 14, 21));
        var rows = new StackPanel();
        Row(DataLabel("usage.date"), "Tokens", DataLabel("usage.spend"), DataLabel("common.requests"));
        foreach (var day in daily.Reverse())
        {
            Row((string?)day["day"] ?? string.Empty, DataCount(Quantity(day, "total")), Money(day["cost"]), DataCount(Quantity(day, "requests")), day["estimateCost"] is JsonArray { Count: > 0 } ? Money(day["estimateCost"]) : null);
        }

        table.Children.Add(new ScrollViewer { MaxHeight = 320, Content = rows, Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(DataFrame(table, new Thickness(18, 14, 18, 12)));
        var split = new DataColumns(12, 700);
        panel.Children.Add(split);
        Breakdown("usage.by_model", "model", r["models"] as JsonArray);
        Breakdown("common.by_source", "provider", r["providers"] as JsonArray);

        void Breakdown(string key, string field, JsonArray? data)
        {
            var block = new StackPanel();
            block.Children.Add(DataText(DataLabel(key), 14, field == "provider" ? 17 : 20));
            var list = new StackPanel { Spacing = 10, Margin = new Thickness(0, 8, 0, 0) };
            foreach (var item in data ?? [])
            {
                var row = new StackPanel { Spacing = 4 };
                var id = (string?)item?[field];
                var provider = field == "provider" ? providers.FirstOrDefault(p => (string?)p?["name"] == id) : null;
                var missing = string.IsNullOrEmpty(id) || (field == "provider" && provider is null);
                var label = missing ? DataLabel("usage.unknown_source") : id!;
                if (provider is not null && Uri.TryCreate((string?)provider["baseUrl"], UriKind.Absolute, out var endpoint))
                {
                    label = (string?)provider["kind"] + " · " + endpoint.Host;
                }

                row.Children.Add(DataText(label + "    " + DataCount(Quantity(item, "tokens")) + "  " + Quantity(item, "percent").ToString("0.0", CultureInfo.CurrentCulture) + "%", 12, missing ? 17 : 15));
                row.Children.Add(new ProgressBar { Height = 6, Minimum = 0, Maximum = 100, Value = Quantity(item, "percent") });
                list.Children.Add(row);
            }

            block.Children.Add(list);
            split.Children.Add(DataFrame(block, new Thickness(18, 16, 18, 12)));
        }

        void Row(string a, string b, string c, string d, string? estimate = null)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,2*,*"), Margin = new Thickness(10, 0) };
            var j = 0;
            foreach (var text in new[] { a, b, estimate is null ? c : c + "\n" + DataLabel("usage.api_priced_estimate") + " " + estimate, d })
            {
                DataColumn(grid, j++, DataText(text, 12, 17));
            }

            rows.Children.Add(new Border { Child = grid, Padding = new Thickness(0, 8), BorderThickness = new Thickness(0, 0, 0, 1) });
        }

        StackPanel ChartPanel(Panel parent, string key, string? code = null)
        {
            var stack = new StackPanel();
            var head = DataText(DataLabel(key) + (code is null ? string.Empty : "   " + code), 14, 21);
            head.Margin = new Thickness(0, 0, 0, 8);
            stack.Children.Add(head);
            var frame = DataFrame(stack, new Thickness(18, 16, 18, 12));
            frame.VerticalAlignment = VerticalAlignment.Top;
            parent.Children.Add(frame);
            return stack;
        }

        StackPanel Tile(string name, string value, bool primary = false)
        {
            var stack = new StackPanel();
            stack.Children.Add(DataText(name, 12, 17));
            var number = DataText(value, primary ? 26 : 22, primary ? 28.59375 : 24.1875);
            number.Margin = new Thickness(0, 6, 0, 0);
            stack.Children.Add(number);
            tiles.Children.Add(DataFrame(stack, new Thickness(16, 14)));
            return stack;
        }

        static void Note(StackPanel stack, string text)
        {
            var note = DataText(text, 12, 17, true);
            note.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(note);
        }

        static double Amount(JsonObject day, string code)
        {
            var value = day["cost"]?.AsArray().FirstOrDefault(m => (string?)m?["currency"] == code)?["amount"]?.ToString();
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0;
        }
    }
}
