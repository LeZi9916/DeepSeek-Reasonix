using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Shell.Host;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Gets or sets a value indicating whether the device hides migrated sessions in the rail.</summary>
    internal bool HideLegacySessions
    {
        get; set;
    }

    /// <summary>Returns the localized name and description of a storage root.</summary>
    /// <param name="id">Stable kernel identifier.</param>
    /// <returns>Localized title and hint.</returns>
    private (string Title, string Hint) RootCopy(string id) => id switch
    {
        "state" => (DataLabel("data.sessions_and_archives"), DataLabel("data.transcripts_compaction_archives_usage_stats")),
        "cache" => (DataLabel("data.indexes_and_caches"), DataLabel("data.search_indexes_and_derived_data")),
        "worktrees" => (DataLabel("data.isolated_worktrees"), DataLabel("data.the_separate_checkouts_delivery_works")),
        "home" => (DataLabel("data.configuration_and_credentials"), DataLabel("data.settings_and_api_keys_these")),
        "locks" => (DataLabel("data.process_locks"), DataLabel("data.how_instances_exclude_each_other")),
        _ => (id, string.Empty),
    };

    /// <summary>Renders import, capacity, movable roots and device-local archive visibility.</summary>
    /// <param name="card">Parent settings card.</param>
    /// <param name="api">Active kernel client.</param>
    /// <returns>The completed operation.</returns>
    private async Task AddStorageAsync(StackPanel card, StudioPageClient api)
    {
        var token = _closing.Token;
        var storage = await api.StorageAsync(token).ConfigureAwait(true);
        var roots = storage["roots"]?.AsArray().OfType<JsonObject>().ToArray() ?? [];
        var group = DataGroup(card, "common.storage", "settings.storage_hint");
        var blocks = new StackPanel { Spacing = 16 };
        group.Children.Add(blocks);
        var status = DataText(string.Empty, 12, 18, true);
        status.IsVisible = false;
        var native = Box("data.import_a_native_session");
        Item(native, DataLabel("data.import_from_a_session_file"), DataLabel("data.keep_the_composition_version_and"), null, null, DataLabel("data.choose_a_session_file"), async () =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false, Title = DataLabel("data.choose_a_session_json_file") }).ConfigureAwait(true);
            if (files.Count > 0)
            {
                await using var stream = await files[0].OpenReadAsync().ConfigureAwait(true);
                var result = await api.ImportSessionAsync(stream, _workspaceRoot, token).ConfigureAwait(true);
                await LoadTreeAsync().ConfigureAwait(true);
                Tell((string?)result["path"] ?? DataLabel("common.done"));
            }
        });
        var legacy = Box("data.recover_older_sessions");
        Item(legacy, DataLabel("data.import_from_an_older_reasonix"), DataLabel("data.choose_the_older_data_folder"), null, null, DataLabel("data.choose_older_data_folder"), async () =>
        {
            var path = await new AvaloniaHost(this).PickFolderAsync(string.Empty).ConfigureAwait(true);
            if (path is not null)
            {
                var result = await api.ImportLegacyAsync(path, _workspaceRoot, token).ConfigureAwait(true);
                await LoadTreeAsync().ConfigureAwait(true);
                Tell(result.ToJsonString());
            }
        });
        var usage = Box("data.space_used");
        var peak = roots.Select(r => Quantity(r, "bytes")).DefaultIfEmpty().Max();
        foreach (var r in roots.Where(r => (string?)r["id"] != "locks" || Quantity(r, "bytes") > 0))
        {
            var stack = new StackPanel { Spacing = 5, Margin = new Thickness(0, 0, 0, 12) };
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            line.Children.Add(DataText(RootCopy((string)r["id"]!).Title));
            DataColumn(line, 1, DataText(DataBytes(Quantity(r, "bytes")) + " · " + DataLabel("common.n_files", ("n", Quantity(r, "files").ToString(CultureInfo.CurrentCulture)))));
            stack.Children.Add(line);
            stack.Children.Add(new ProgressBar { Minimum = 0, Maximum = Math.Max(1, peak), Value = Quantity(r, "bytes"), Height = 5 });
            usage.Children.Add(stack);
        }

        foreach (var r in roots.Where(r => Quantity(r, "volumeTotal") > 0).DistinctBy(r => (string?)r["volume"]))
        {
            usage.Children.Add(DataText(DataLabel("data.drive_free_free_of_total", ("drive", (string?)r["volume"] ?? string.Empty), ("free", DataBytes(Quantity(r, "volumeFree"))), ("total", DataBytes(Quantity(r, "volumeTotal")))), 12, 17, true));
        }

        var locations = Box("data.locations");
        foreach (var root in roots)
        {
            var id = (string)root["id"]!;
            var labels = RootCopy(id);
            var movable = (bool?)storage["editable"] == true && (bool?)root["relocatable"] == true && string.IsNullOrEmpty((string?)root["pinnedBy"]);
            var target = new StackPanel { Spacing = 6 };
            Item(locations, labels.Title, labels.Hint, (string?)root["dir"], movable ? null : DataLabel("data.cannot_be_moved"), movable ? DataLabel("data.move") : null, async () =>
            {
                var dest = await new AvaloniaHost(this).PickFolderAsync((string?)root["dir"] ?? string.Empty).ConfigureAwait(true);
                if (dest is null)
                {
                    return;
                }

                var plan = await api.MoveStorageAsync(id, dest, false, token).ConfigureAwait(true);
                target.Children.Clear();
                target.Children.Add(DataText((string?)plan["error"] ?? (dest + " · " + DataBytes(Quantity(plan, "bytes"))), 12, 18, true));
                if ((bool?)plan["ok"] == true)
                {
                    Action(target, DataLabel("data.move"), async () =>
                    {
                        var result = await api.MoveStorageAsync(id, dest, true, token).ConfigureAwait(true);
                        Tell((string?)result["error"] ?? DataLabel("data.moved_it_takes_effect_after"));
                        target.Children.Clear();
                    });
                }

                var cancel = DataButton(DataLabel("common.cancel"));
                cancel.Click += (_, _) => target.Children.Clear();
                target.Children.Add(cancel);
            });
            locations.Children.Add(target);
        }

        group.Children.Add(status);
        var policy = await api.ArchivePolicyAsync(token).ConfigureAwait(true);
        var days = new NumericUpDown { Width = 62, Height = 28, MinHeight = 0, Minimum = 1, Maximum = 3650, Value = (int?)policy["days"] ?? 30, ShowButtonSpinner = false };
        var enabled = new ToggleSwitch { OnContent = string.Empty, OffContent = string.Empty, MinHeight = 0, MinWidth = 0, IsChecked = (bool?)policy["enabled"] == true };
        var fields = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Height = 28 };
        var unit = new Grid { Width = 96, ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        unit.Children.Add(days);
        DataColumn(unit, 1, DataText(DataLabel("data.days"), 12, 28));
        fields.Children.Add(unit);
        enabled.Width = 34;
        fields.Children.Add(enabled);
        DataRow(card, "data.auto_archive_inactive_conversations", "settings.days_before_archiving", fields);
        enabled.Click += async (_, _) => await SaveArchive().ConfigureAwait(true);
        days.LostFocus += async (_, _) =>
        {
            if (days.Value != (int?)policy["days"])
            {
                await SaveArchive().ConfigureAwait(true);
            }
        };
        var pathText = DataText(_engine is null ? _workspaceRoot : (await _engine.StatusAsync(token).ConfigureAwait(true))?.Cwd ?? _workspaceRoot, 12, 30);
        pathText.FontFamily = FontFamily.Parse("monospace");
        pathText.MaxWidth = 260;
        pathText.Padding = new Thickness(4, 0);
        pathText.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTip.SetTip(pathText, _workspaceRoot);
        DataRow(card, "session.where_this_session_writes", "settings.where_session_records_go", pathText);
        var isolated = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10, Margin = new Thickness(16, 9, 0, 15) };
        var description = new StackPanel { Spacing = 2 };
        description.Children.Add(DataText(DataLabel("session.work_on_an_isolated_copy")));
        description.Children.Add(DataText(DataLabel("session.work_in_a_separate_git"), 13, 18.59375, true));
        isolated.Children.Add(description);
        var isolate = DataButton(DataLabel("session.open_one"), 24);
        isolate.Click += async (_, _) => await Run(isolate, async () =>
        {
            await api.IsolateAsync(token).ConfigureAwait(true);
            if (_engine is not null && await _engine.StatusAsync(token).ConfigureAwait(true) is { } current)
            {
                AdoptComposer(current);
            }

            await LoadTreeAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
        DataColumn(isolated, 1, isolate);
        card.Children.Add(isolated);
        var visibility = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var hidden in new[] { false, true })
        {
            var b = new RadioButton { Content = DataLabel(hidden ? "data.hide" : "common.show"), IsChecked = HideLegacySessions == hidden };
            b.Classes.Add("segmented");
            b.Click += async (_, _) => await Run(b, async () =>
            {
                HideLegacySessions = hidden;
                SaveWindowPreferences?.Invoke();
                await LoadTreeAsync().ConfigureAwait(true);
            }).ConfigureAwait(true);
            visibility.Children.Add(b);
        }

        DataRow(card, "data.sessions_from_1_x", "settings.migrated_sessions_in_the_sidebar", visibility);

        async Task SaveArchive()
        {
            if (!fields.IsEnabled)
            {
                return;
            }

            fields.IsEnabled = false;
            try
            {
                policy = await api.SetArchivePolicyAsync(enabled.IsChecked == true, (int)(days.Value ?? 30), token).ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                Tell(DescribeError(error));
            }
            finally
            {
                enabled.IsChecked = (bool?)policy["enabled"] == true;
                days.Value = (int?)policy["days"] ?? 30;
                fields.IsEnabled = true;
            }
        }

        StackPanel Box(string title)
        {
            var box = new StackPanel();
            var header = DataText(DataLabel(title));
            header.FontWeight = FontWeight.SemiBold;
            header.Margin = new Thickness(0, 13);
            box.Children.Add(header);
            var frame = DataFrame(box, new Thickness(22, 20, 22, 18));
            frame.SizeChanged += (_, e) => frame.Padding = e.NewSize.Width <= 620 ? new Thickness(15, 17, 15, 15) : new Thickness(22, 20, 22, 18);
            blocks.Children.Add(frame);
            return box;
        }

        void Item(StackPanel parent, string title, string hint, string? path, string? held, string? action, Func<Task> click)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 14 };
            var copy = new StackPanel { Spacing = 2 };
            var name = DataText(title);
            name.FontWeight = FontWeight.SemiBold;
            copy.Children.Add(name);
            var help = DataText(hint, 12, 18, true);
            help.Margin = new Thickness(0, 2, 0, 0);
            copy.Children.Add(help);
            if (path is not null)
            {
                var location = DataText(path, 12, 15, true);
                location.Bind(TextBlock.FontFamilyProperty, location.GetResourceObservable("DataPathFamily"));
                copy.Children.Add(location);
            }

            if (held is not null)
            {
                copy.Children.Add(DataText(held, 12, 17));
            }

            row.Children.Add(copy);
            if (action is not null)
            {
                var button = DataButton(action, 32);
                button.FontSize = 13;
                button.FontWeight = FontWeight.Bold;
                button.Padding = new Thickness(14, 0);
                button.VerticalAlignment = VerticalAlignment.Top;
                button.Click += async (_, _) => await Run(button, click).ConfigureAwait(true);
                DataColumn(row, 1, button);
            }

            var item = DataFrame(row, new Thickness(12, 10));
            item.Margin = new Thickness(0, 0, 0, 8);
            parent.Children.Add(item);
        }

        void Action(StackPanel parent, string label, Func<Task> click)
        {
            var button = DataButton(label);
            button.Click += async (_, _) => await Run(button, click).ConfigureAwait(true);
            parent.Children.Add(button);
        }

        void Tell(string text)
        {
            status.Text = text;
            status.IsVisible = true;
        }

        async Task Run(Control action, Func<Task> callback)
        {
            if (!action.IsEnabled)
            {
                return;
            }

            action.IsEnabled = false;
            try
            {
                await callback().ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                Tell(DescribeError(error));
            }
            finally
            {
                action.IsEnabled = true;
            }
        }
    }
}
