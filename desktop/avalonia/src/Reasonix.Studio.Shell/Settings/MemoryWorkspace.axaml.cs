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
    /// <param name="describe">Localizes kernel failures.</param>
    /// <param name="token">Window lifetime.</param>
    internal void Open(StudioPageClient api, InterfaceLanguage language, JsonObject catalog, Func<Exception, string> describe, CancellationToken token)
    {
        Translate(language);
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
            Save.IsEnabled = History.IsEnabled = !flags.Contains(selected);
            Heading.Text = (string?)m["title"] ?? selected;
            Summary.Text = (string?)m["description"];
            Prose.Text = (string?)m["body"] ?? Text("memory.no_body");
            Facts.Text = string.Join(" · ", new[] { types.First(o => o.Id == TypeOf(m)).Label, (string?)m["galaxy"], (string?)m["path"] }.Where(v => !string.IsNullOrWhiteSpace(v)));
            var revision = (int?)m["revision"] ?? 1;
            History.IsVisible = revision > 1;
            History.Content = Text("memory.revision_n_see_older", ("n", revision.ToString(CultureInfo.CurrentCulture)));
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
            foreach (var g in items.Select(m => (string?)m["galaxy"]).Where(g => !string.IsNullOrEmpty(g)).Distinct().OrderBy(g => g))
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
                var option = new RadioButton { Content = o.Label + "  " + items.Count(o.Matches), IsChecked = o.Id == filter, HorizontalAlignment = HorizontalAlignment.Stretch };
                option.Classes.Add("segmented");
                AutomationProperties.SetAutomationId(option, "action:memory.filter");
                option.Click += (_, _) =>
                {
                    filter = o.Id;
                    shown = 30;
                    Render();
                };
                Categories.Children.Add(option);
            }

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
                copy.Children.Add(new TextBlock { Text = (string?)m["title"] ?? name, FontWeight = FontWeight.Medium, TextWrapping = TextWrapping.Wrap });
                copy.Children.Add(Caption(types.First(o => o.Id == TypeOf(m)).Label + " · " + ((string?)m["galaxy"] ?? Text("memory.no_galaxy"))));
                var button = new Button { Content = copy, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 6) };
                button.Bind(Button.BackgroundProperty, button.GetResourceObservable(name == selected ? "RaisedBrush" : "SurfaceBrush"));
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

        Categories.Orientation = narrow ? Orientation.Horizontal : Orientation.Vertical;
        Grid.SetColumn(Catalog, narrow ? 0 : 1);
        Grid.SetRow(Catalog, narrow ? 1 : 0);
        Grid.SetColumn(DetailPane, stacked ? 0 : 2);
        Grid.SetColumnSpan(DetailPane, stacked && !narrow ? 2 : 1);
        Grid.SetRow(DetailPane, narrow ? 2 : stacked ? 1 : 0);
    }
}
