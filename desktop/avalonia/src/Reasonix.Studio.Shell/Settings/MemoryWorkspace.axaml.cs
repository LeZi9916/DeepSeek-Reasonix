using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>The searchable memory catalog, its editor and its append-only history.</summary>
public sealed partial class MemoryWorkspace : SettingsFormView
{
    /// <summary>Resolves the mounted editor's outstanding changes.</summary>
    private Func<bool>? _dirty;

    /// <summary>Initializes a new instance of the <see cref="MemoryWorkspace"/> class.</summary>
    public MemoryWorkspace()
    {
        InitializeComponent();
        Columns.SizeChanged += (_, e) => LayoutColumns(e.NewSize.Width);
    }

    /// <summary>Gets a value indicating whether leaving the settings sheet would discard edits.</summary>
    internal bool HasChanges => _dirty?.Invoke() == true;

    /// <summary>Connects this instance to the machine whose catalog is displayed.</summary>
    /// <param name="api">The active kernel.</param>
    /// <param name="language">The reader's language.</param>
    /// <param name="catalog">Initial memory snapshot.</param>
    /// <param name="policy">The host's proactive policy, or null when unsupported.</param>
    /// <param name="describe">Localizes kernel failures.</param>
    /// <param name="token">Window lifetime.</param>
    internal void Open(StudioPageClient api, InterfaceLanguage language, JsonObject catalog, string? policy, Func<Exception, string> describe, CancellationToken token)
    {
        Translate(language);
        PolicyRow.IsVisible = policy is not null;
        ManualHint.Text = Text("memory.to_write_one_yourself_use", ("cmd", "/remember"));
        Decorate(Confirm, "check", Text("memory.thats_right"));
        Decorate(Edit, "edit", Text("common.edit"));
        Decorate(Archive, "archive", string.Empty);
        PolicyChoices.Children.Clear();
        foreach (var (id, key) in new[] { ("off", "memory.proactive_off"), ("ask", "memory.proactive_ask"), ("auto", "memory.proactive_auto") })
        {
            var button = new RadioButton { Content = Text(key), Tag = id, IsChecked = id == policy, Height = 29, MinWidth = 0, Margin = default, Padding = new Thickness(12, 0), FontSize = 12 };
            button.Classes.Add("segmented");
            AutomationProperties.SetAutomationId(button, "action:memory.proactive");
            button.Click += async (_, _) =>
            {
                if (policy == id || !PolicyChoices.IsEnabled)
                {
                    return;
                }

                PolicyChoices.IsEnabled = false;
                try
                {
                    await api.SetMemoryPolicyAsync(id, token).ConfigureAwait(true);
                    policy = await api.MemoryPolicyAsync(token).ConfigureAwait(true);
                    PolicyRow.IsVisible = policy is not null;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
                catch (Exception error)
                {
                    Error(describe(error));
                }
                finally
                {
                    foreach (var choice in PolicyChoices.Children.OfType<RadioButton>())
                    {
                        choice.IsChecked = Equals(choice.Tag, policy);
                    }

                    PolicyChoices.IsEnabled = true;
                }
            };
            PolicyChoices.Children.Add(button);
        }

        var flags = new HashSet<string>(StringComparer.Ordinal);
        var selected = string.Empty;
        string? editing = null;
        JsonObject? original = null;
        var editEpoch = 0;
        var historyEpoch = 0;
        var reloadEpoch = 0;
        var shown = 30;
        var filter = "all";
        var types = new[] { new Option("constraint", Text("memory.constraints")), new Option("user", Text("remote.user")), new Option("feedback", Text("feedback.feedback")), new Option("project", Text("common.project")), new Option("reference", Text("memory.reference")) };
        Kind.ItemsSource = types;
        Activation.ItemsSource = new[] { new Option("pinned", Text("memory.in_every_turn")), new Option("relevant", Text("memory.recalled_when_relevant")) };
        Importance.ItemsSource = new[] { new Option("3", Text("memory.high")), new Option("2", Text("memory.medium")), new Option("1", Text("memory.low")) };
        _dirty = () => editing is not null && !JsonNode.DeepEquals(original, Draft());
        Search.TextChanged += (_, _) =>
        {
            shown = 30;
            Render();
        };
        Edit.Click += (_, _) =>
        {
            if (Current() is not { } m)
            {
                return;
            }

            editing = selected;
            editEpoch++;
            Title.Text = (string?)m["title"] ?? string.Empty;
            Description.Text = (string?)m["description"] ?? string.Empty;
            Body.Text = (string?)m["body"] ?? string.Empty;
            Galaxy.Text = (string?)m["galaxy"] ?? string.Empty;
            Select(Kind, TypeOf(m));
            Select(Importance, ((int?)m["importance"] ?? 2).ToString(CultureInfo.InvariantCulture));
            Select(Activation, (string?)m["activation"] ?? "relevant");
            original = Draft();
            ShowDetail();
            Title.Focus();
        };
        Confirm.Click += async (_, _) =>
        {
            var name = selected;
            await WriteAsync(name, () => api.ConfirmMemoryAsync(name, token)).ConfigureAwait(true);
        };
        Archive.Click += async (_, _) =>
        {
            var name = selected;
            await WriteAsync(name, async () =>
            {
                await api.ArchiveMemoryAsync(name, token).ConfigureAwait(true);
                if (editing == name)
                {
                    editing = null;
                    editEpoch++;
                }
            }).ConfigureAwait(true);
        };
        Cancel.Click += (_, _) =>
        {
            editing = null;
            editEpoch++;
            ShowDetail();
            Edit.Focus();
        };
        Save.Click += async (_, _) =>
        {
            if (editing is null)
            {
                return;
            }

            var name = editing;
            var epoch = editEpoch;
            var draft = Draft();
            await WriteAsync(name, async () =>
            {
                await api.SaveMemoryAsync(draft, token).ConfigureAwait(true);
                if (editEpoch == epoch && editing == name && JsonNode.DeepEquals(draft, Draft()))
                {
                    editing = null;
                }
            }).ConfigureAwait(true);
        };
        History.Click += async (_, _) =>
        {
            if (Revisions.IsVisible)
            {
                ShowDetail();
                return;
            }

            var ticket = ++historyEpoch;
            var name = selected;
            Revisions.Children.Clear();
            Revisions.Children.Add(Caption(Text("memory.reading_older_revisions")));
            Revisions.IsVisible = true;
            History.Content = Text("memory.hide_older_revisions");
            try
            {
                var revisions = await api.MemoryRevisionsAsync(name, token).ConfigureAwait(true);
                if (historyEpoch != ticket)
                {
                    return;
                }

                var revision = (int?)Current()?["revision"] ?? 1;
                Revisions.Children.Clear();
                foreach (var old in revisions.OfType<JsonObject>().Where(m => ((int?)m["revision"] ?? 1) != revision))
                {
                    var number = (int?)old["revision"] ?? 1;
                    var entry = new StackPanel { Spacing = 3, Margin = new Thickness(0, 6) };
                    entry.Children.Add(Caption(Text("memory.revision_n", ("n", number.ToString(CultureInfo.CurrentCulture))) + " · " + (string?)old["title"] + "  " + (string?)old["updatedAt"]));
                    entry.Children.Add(Caption((string?)old["body"] ?? Text("memory.no_body")));
                    var restore = new Button { Content = Text("memory.restore_this_one") };
                    AutomationProperties.SetAutomationId(restore, "action:memory.restore");
                    restore.Click += async (_, _) => await WriteAsync(name, () => api.RestoreMemoryAsync(name, number, token)).ConfigureAwait(true);
                    entry.Children.Add(restore);
                    Revisions.Children.Add(entry);
                }

                Revisions.Children.Add(Caption(Text(Revisions.Children.Count == 0 ? "memory.only_the_current_revision_exists" : "memory.restoring_records_a_new_revision")));
            }
            catch (Exception error)
            {
                if (ticket == historyEpoch && !token.IsCancellationRequested)
                {
                    ShowDetail();
                    Error(describe(error));
                }
            }
        };
        Reload.Click += async (_, _) => await RefreshAsync().ConfigureAwait(true);
        Render();

        IEnumerable<JsonObject> Items() => catalog["memories"]?.AsArray().OfType<JsonObject>() ?? [];
        JsonObject? Current() => Items().FirstOrDefault(m => (string?)m["name"] == selected);
        string TypeOf(JsonObject m) => types.Any(o => o.Id == (string?)m["type"]) ? (string)m["type"]! : "project";

        JsonObject Draft() => new()
        {
            ["name"] = editing,
            ["title"] = Title.Text ?? string.Empty,
            ["description"] = Description.Text ?? string.Empty,
            ["body"] = Body.Text ?? string.Empty,
            ["galaxy"] = Galaxy.Text ?? string.Empty,
            ["type"] = ((Option?)Kind.SelectedItem)?.Id,
            ["importance"] = int.Parse(((Option?)Importance.SelectedItem)?.Id ?? "2", CultureInfo.InvariantCulture),
            ["activation"] = ((Option?)Activation.SelectedItem)?.Id,
        };

        void Error(string message)
        {
            Failure.Text = message;
            Failure.IsVisible = message.Length > 0;
        }

        async Task RefreshAsync()
        {
            var ticket = ++reloadEpoch;
            try
            {
                var fresh = await api.MemoriesAsync(token).ConfigureAwait(true);
                if (ticket == reloadEpoch)
                {
                    catalog = fresh;
                    Reload.IsVisible = false;
                    Error(string.Empty);
                    Render();
                }
            }
            catch (Exception error)
            {
                if (ticket == reloadEpoch && !token.IsCancellationRequested)
                {
                    Error(describe(error));
                    Reload.IsVisible = true;
                }
            }
        }

        async Task WriteAsync(string name, Func<Task> write)
        {
            if (!flags.Add(name))
            {
                return;
            }

            Error(string.Empty);
            ShowDetail();
            try
            {
                await write().ConfigureAwait(true);
                await RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    Error(describe(error));
                }
            }
            finally
            {
                flags.Remove(name);
                ShowDetail();
            }
        }

        void ShowDetail()
        {
            historyEpoch++;
            Revisions.IsVisible = false;
            Revisions.Children.Clear();
            var m = Current();
            DetailPane.IsVisible = m is not null;
            if (m is null)
            {
                return;
            }

            Editor.IsVisible = selected == editing;
            Reader.IsVisible = !Editor.IsVisible;
            Save.IsEnabled = History.IsEnabled = Confirm.IsEnabled = Archive.IsEnabled = !flags.Contains(selected);
            Heading.Text = (string?)m["title"] ?? selected;
            Summary.Text = (string?)m["description"];
            Prose.Text = (string?)m["body"] ?? Text("memory.no_body");
            Facts.Text = (string?)m["path"];
            Facts.IsVisible = !string.IsNullOrEmpty(Facts.Text);
            ContentFacts.Children.Clear();
            UseFacts.Children.Clear();
            Fact(ContentFacts, "common.type", types.First(o => o.Id == TypeOf(m)).Label);
            Fact(ContentFacts, "memory.galaxy", (string?)m["galaxy"] ?? Text("memory.no_galaxy"));
            Fact(ContentFacts, "memory.importance", Text((int?)m["importance"] switch
            {
                1 => "memory.low",
                3 => "memory.high",
                _ => "memory.medium",
            }));
            Fact(ContentFacts, "memory.scope", (string?)m["scope"] switch
            {
                "project" => Text("session.this_project"),
                "global" => Text("memory.all_projects"),
                var scope => scope ?? "—",
            });
            var stateKey = (string?)m["state"] switch
            {
                "fuzzy" => "memory.a_little_fuzzy_using_it",
                "faded" => "memory.faded_recalled_after_everything_else",
                _ when (bool?)m["noDecay"] == true => "memory.clear_and_it_never_fades",
                _ when (int?)m["clearForDays"] > 0 => "memory.clear_for_about_n_more",
                _ => "memory.clear",
            };
            Fact(UseFacts, "memory.state", Text(stateKey, ("n", ((int?)m["clearForDays"] ?? 0).ToString(CultureInfo.CurrentCulture))));
            Fact(UseFacts, "session.recent", (string?)m["lastUsedAt"] ?? Text("memory.not_recalled_yet"));
            Fact(UseFacts, "memory.used", Text("memory.n_times", ("n", ((int?)m["uses"] ?? 0).ToString(CultureInfo.CurrentCulture))));
            Fact(UseFacts, "memory.applies", Text((string?)m["activation"] == "pinned" ? "memory.in_every_turn" : "memory.recalled_when_relevant_2"));
            var revision = (int?)m["revision"] ?? 1;
            History.IsVisible = revision > 1;
            History.Content = Text("memory.revision_n_see_older", ("n", revision.ToString(CultureInfo.CurrentCulture)));
        }

        void Fact(StackPanel group, string key, string value)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("56,*"), ColumnSpacing = 10 };
            var label = new DataParagraph { Text = Text(key), FontSize = 12, LineHeight = 17.390625, TextWrapping = TextWrapping.Wrap };
            label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("FaintBrush"));
            var content = new DataParagraph { Text = value, FontSize = 12, LineHeight = 17.390625, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(content, 1);
            row.Children.Add(label);
            row.Children.Add(content);
            group.Children.Add(row);
        }

        void Render()
        {
            var items = Items().ToArray();
            Warnings.Children.Clear();
            foreach (var failure in catalog["unreadable"]?.AsArray().OfType<JsonObject>() ?? [])
            {
                Warnings.Children.Add(Caption((string?)failure["name"] + " · " + (string?)failure["error"]));
            }

            var options = new List<(string Id, string Label, Func<JsonObject, bool> Matches)>
            {
                ("all", Text("common.all"), _ => true), ("new", Text("memory.new"), m => (bool?)m["new"] == true),
            };
            options.AddRange(types.Select(o => (o.Id, o.Label, (Func<JsonObject, bool>)(m => TypeOf(m) == o.Id))));
            foreach (var g in items.Select(m => (string?)m["galaxy"]).Where(g => !string.IsNullOrEmpty(g)).GroupBy(g => g).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.CurrentCulture).Select(g => g.Key))
            {
                options.Add(("galaxy:" + g, g!, m => (string?)m["galaxy"] == g));
            }

            options.Add(("faded", Text("memory.faded"), m => (string?)m["state"] == "faded" && (bool?)m["noDecay"] != true));
            var currentFilter = options.FirstOrDefault(o => o.Id == filter);
            if (currentFilter.Matches is null)
            {
                currentFilter = options[0];
                filter = currentFilter.Id;
            }

            Categories.Children.Clear();
            foreach (var o in options)
            {
                if (o.Id == "user" || o.Id == "faded" || (o.Id.StartsWith("galaxy:", StringComparison.Ordinal) && !Categories.Children.OfType<TextBlock>().Any(c => Equals(c.Tag, "galaxies"))))
                {
                    var section = new TextBlock
                    {
                        Text = o.Id == "user" ? Text("common.type") : o.Id == "faded" ? string.Empty : Text("memory.galaxy"),
                        Tag = o.Id == "user" ? "types" : o.Id == "faded" ? "end" : "galaxies",
                        Height = o.Id == "faded" ? 10 : 28,
                        Padding = new Thickness(10, 8, 0, 0),
                        FontSize = 10,
                        FontWeight = FontWeight.Medium,
                    };
                    section.Bind(TextBlock.ForegroundProperty, section.GetResourceObservable("FaintBrush"));
                    Categories.Children.Add(section);
                }

                var icon = o.Id.StartsWith("galaxy:", StringComparison.Ordinal)
                    ? "galaxy"
                    : o.Id switch
                    {
                        "new" => "fresh",
                        "user" => "account",
                        "project" => "folder",
                        _ => o.Id,
                    };
                var content = new Grid { ColumnDefinitions = new ColumnDefinitions("16,*,Auto"), ColumnSpacing = 8 };
                content.Children.Add(Glyph(icon, 16));
                var label = new TextBlock { Text = o.Label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(label, 1);
                content.Children.Add(label);
                var count = new TextBlock { Text = items.Count(o.Matches).ToString(CultureInfo.CurrentCulture), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                count.MinWidth = count.Text!.Length * 7.78125;
                count.Bind(TextBlock.ForegroundProperty, count.GetResourceObservable("FaintBrush"));
                Grid.SetColumn(count, 2);
                content.Children.Add(count);
                var option = new RadioButton { Content = content, IsChecked = o.Id == filter, HorizontalAlignment = HorizontalAlignment.Stretch };
                option.Classes.Add("memory-nav");
                AutomationProperties.SetName(option, o.Label);
                AutomationProperties.SetAutomationId(option, "action:memory.filter");
                option.Click += (_, _) =>
                {
                    filter = o.Id;
                    shown = 30;
                    Render();
                };
                Categories.Children.Add(option);
            }

            FormatRail();
            var query = (string?)catalog["recallQuery"];
            var used = items.Count(m => (bool?)m["usedLastTurn"] == true);
            Recall.IsVisible = !string.IsNullOrWhiteSpace(query);
            Recall.Text = Text("memory.last_turn_searched_memory_from", ("q", query ?? string.Empty)) + Text(used > 0 ? "memory.n_of_them_used" : "memory.none_of_them_used", ("n", used.ToString(CultureInfo.CurrentCulture)));
            var terms = (Search.Text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var matching = items.Where(currentFilter.Matches).Where(m =>
            {
                var haystack = string.Join("\n", new[] { "name", "title", "description", "body", "galaxy" }.Select(key => (string?)m[key]));
                return terms.All(term => haystack.Contains(term, StringComparison.CurrentCultureIgnoreCase));
            }).OrderByDescending(m => (string?)m["lastUsedAt"] ?? (string?)m["updatedAt"] ?? (string?)m["createdAt"], StringComparer.Ordinal)
                .ThenBy(m => (string?)m["title"] ?? (string?)m["name"], StringComparer.CurrentCulture).ToArray();
            if (!matching.Any(m => (string?)m["name"] == selected))
            {
                selected = filter == "new" ? string.Empty : (string?)matching.FirstOrDefault()?["name"] ?? string.Empty;
            }

            Entries.Children.Clear();
            foreach (var m in matching.Take(shown))
            {
                var name = (string)m["name"]!;
                var copy = new StackPanel { Spacing = 2 };
                var titleLine = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6, Height = 18.1875 };
                titleLine.Children.Add(new TextBlock { Text = (string?)m["title"] ?? name, FontSize = 13, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                var badge = (bool?)m["new"] == true
                    ? "common.new"
                    : (string?)m["state"] switch
                    {
                        "fuzzy" => "memory.fading",
                        "faded" => "memory.faded",
                        _ => null,
                    };
                if (badge is not null)
                {
                    var text = new TextBlock { Text = Text(badge), FontSize = 10, FontWeight = FontWeight.SemiBold, LineHeight = 16, VerticalAlignment = VerticalAlignment.Center };
                    text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("AccentFgBrush"));
                    var status = new Border { Child = text, CornerRadius = new CornerRadius(999), Padding = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center };
                    status.Bind(Border.BackgroundProperty, status.GetResourceObservable("AccentBrush"));
                    Grid.SetColumn(status, 1);
                    titleLine.Children.Add(status);
                }

                copy.Children.Add(titleLine);
                copy.Children.Add(new TextBlock { Text = types.First(o => o.Id == TypeOf(m)).Label + " · " + ((string?)m["galaxy"] ?? Text("memory.no_galaxy")), FontSize = 10, Height = 16, TextTrimming = TextTrimming.CharacterEllipsis });
                var button = new Button { Content = copy, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 9), Margin = new Thickness(0, 0, 8, 0) };
                button.Classes.Add("memory-entry");
                button.Bind(Button.BackgroundProperty, button.GetResourceObservable(name == selected ? "AccentWashBrush" : "SurfaceBrush"));
                AutomationProperties.SetAutomationId(button, "action:memory.open");
                AutomationProperties.SetName(button, (string?)m["title"] ?? name);
                button.Click += (_, _) =>
                {
                    selected = name;
                    Render();
                };
                Entries.Children.Add(button);
            }

            if (matching.Length == 0)
            {
                Entries.Children.Add(Caption(Text(items.Length == 0 ? "memory.no_entries_yet" : "memory.no_memories_match")));
            }
            else if (shown < matching.Length)
            {
                var more = new Button { Content = Text("memory.show_n_more_total_in", ("n", Math.Min(30, matching.Length - shown).ToString(CultureInfo.CurrentCulture)), ("total", matching.Length.ToString(CultureInfo.CurrentCulture))) };
                more.Click += (_, _) =>
                {
                    shown += 30;
                    Render();
                };
                Entries.Children.Add(more);
            }

            ShowDetail();
        }
    }

    /// <summary>Selects a field without changing its canonical value.</summary>
    /// <param name="box">Choice control.</param>
    /// <param name="id">Stored identity.</param>
    private static void Select(ComboBox box, string id) => box.SelectedItem = box.Items.OfType<Option>().FirstOrDefault(o => o.Id == id);

    /// <summary>Uses the full category column on desktop and wraps the individual choices on compact windows.</summary>
    private void FormatRail()
    {
        var width = Columns.Bounds.Width;
        var compact = width <= 620;
        foreach (var child in Categories.Children)
        {
            child.IsVisible = !compact || child is RadioButton;
            child.Width = compact ? double.NaN : (width <= 900 ? 170 : 190) - 17;
            child.Margin = compact ? new Thickness(0, 0, 4, 4) : default;
        }

        Categories.Margin = compact ? new Thickness(8, 12, 4, 8) : new Thickness(8, 12);
    }

    /// <summary>Draws a reference icon from the existing Lucide resources.</summary>
    /// <param name="key">Icon name.</param>
    /// <param name="size">Displayed size.</param>
    /// <returns>The stroked icon.</returns>
    private Control Glyph(string key, double size)
    {
        var path = new Avalonia.Controls.Shapes.Path { StrokeThickness = 1.7, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round };
        path.Bind(Avalonia.Controls.Shapes.Path.DataProperty, this.GetResourceObservable("Glyph." + key));
        path.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, this.GetResourceObservable("MutedBrush"));
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    /// <summary>Labels an icon action and sizes its contents like the corresponding Electron button.</summary>
    /// <param name="button">Native action.</param>
    /// <param name="key">Icon name.</param>
    /// <param name="label">Optional visible text.</param>
    private void Decorate(Button button, string key, string label)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(Glyph(key, 14));
        if (label.Length > 0)
        {
            content.Children.Add(new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            button.MinWidth = 64;
        }

        button.Content = content;
    }

    /// <summary>Matches the reference memory panel's two container breakpoints.</summary>
    /// <param name="width">Available panel width.</param>
    private void LayoutColumns(double width)
    {
        var narrow = width <= 620;
        var stacked = width <= 900;
        Columns.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : stacked ? "170,*" : "190,2*,3*");
        if (!stacked)
        {
            Columns.ColumnDefinitions[1].MinWidth = 240;
            Columns.ColumnDefinitions[1].MaxWidth = 340;
        }

        CategoryBorder.BorderThickness = narrow ? new Thickness(0, 0, 0, 1) : new Thickness(0, 0, 1, 0);
        Catalog.BorderThickness = stacked ? default : new Thickness(0, 0, 1, 0);
        DetailPane.Margin = new Thickness(22, stacked ? 19 : 18, 22, 18);
        FormatRail();
        Grid.SetColumn(Catalog, narrow ? 0 : 1);
        Grid.SetRow(Catalog, narrow ? 1 : 0);
        Grid.SetColumn(DetailPane, stacked ? 0 : 2);
        Grid.SetColumnSpan(DetailPane, stacked && !narrow ? 2 : 1);
        Grid.SetRow(DetailPane, narrow ? 2 : stacked ? 1 : 0);
    }
}
