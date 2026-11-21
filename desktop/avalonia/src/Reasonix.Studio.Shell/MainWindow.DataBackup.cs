using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Shell.Host;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Offers encrypted configuration export and a separate, itemized restore preview.</summary>
    /// <param name="card">Parent settings card.</param>
    /// <param name="api">Active kernel client.</param>
    /// <returns>The completed operation.</returns>
    private async Task AddBackupAsync(StackPanel card, StudioPageClient api)
    {
        var token = _closing.Token;
        var catalog = await api.BackupCatalogAsync(token).ConfigureAwait(true);
        var group = DataGroup(card, "data.export_and_import_config", "settings.config_in_one_file");
        var form = new StackPanel { Spacing = 12 };
        group.Children.Add(form);
        form.Children.Add(DataText(DataLabel("data.the_file_is_encrypted_on"), 12, 19.796875, true));
        var chips = new WrapPanel { Margin = new Thickness(0, 0, -6, -6) };
        form.Children.Add(chips);
        var notice = DataText(string.Empty, 12, 19.796875, true);
        notice.IsVisible = false;
        var extra = DataText(DataLabel("data.off_by_default_if_ticked"), 12, 19.796875, true);
        extra.IsVisible = false;
        form.Children.Add(extra);
        var categories = catalog["categories"]?.AsArray().OfType<JsonObject>() ?? [];
        var min = (int?)catalog["minPassphrase"] ?? 10;
        var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 10 };
        var pass = Password(fields, 0, DataLabel("data.passphrase_at_least_n_characters", ("n", min.ToString(CultureInfo.CurrentCulture))));
        var again = Password(fields, 1, DataLabel("data.repeat_it"));
        form.Children.Add(fields);
        var actions = new WrapPanel();
        var export = DataButton(DataLabel("data.export_to_file"), 24);
        var wait = DataText(string.Empty, 12, 19.796875, true);
        wait.VerticalAlignment = VerticalAlignment.Center;
        wait.Margin = new Thickness(8, 0);
        var import = DataButton(DataLabel("data.import_from_file"), 24);
        actions.Children.Add(export);
        actions.Children.Add(wait);
        actions.Children.Add(import);
        form.Children.Add(actions);
        form.Children.Add(notice);
        var restore = new StackPanel { Spacing = 8 };
        form.Children.Add(restore);
        restore.IsVisible = false;
        foreach (var c in categories)
        {
            var id = (string)c["id"]!;
            var key = id switch
            {
                "settings" => "data.models_and_interface",
                "extensions" => "data.skills_plugins_and_mcp",
                "memory" => "data.long_term_memory",
                "automation" => "data.hooks_and_status_line_command",
                "secrets" => "data.api_keys",
                _ => id,
            };
            var check = new CheckBox { Tag = id, Content = DataLabel(key), IsChecked = (bool?)c["defaultOn"] == true, Height = 28, MinHeight = 0, Padding = new Thickness(8, 0), Margin = new Thickness(0, 0, 6, 6) };
            check.IsCheckedChanged += (_, _) => Validate();
            chips.Children.Add(check);
        }

        pass.TextChanged += (_, _) => Validate();
        again.TextChanged += (_, _) => Validate();
        export.Click += async (_, _) => await Run(async () =>
        {
            var result = await api.BackupFileAsync("export", new JsonObject { ["categories"] = JsonSerializer.SerializeToNode(Selected()), ["passphrase"] = pass.Text }, token).ConfigureAwait(true);
            if ((string?)result["file"] is { } envelope && await new AvaloniaHost(this).SaveTextAsync((string?)result["name"] ?? "reasonix.rxcb", envelope).ConfigureAwait(true) is not null)
            {
                pass.Text = again.Text = string.Empty;
                Tell(DataLabel("data.exported"));
            }
        }).ConfigureAwait(true);
        import.Click += async (_, _) => await Run(async () =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false, Title = DataLabel("data.import_from_file") }).ConfigureAwait(true);
            if (files.FirstOrDefault() is not { } file)
            {
                return;
            }

            await using var input = await file.OpenReadAsync().ConfigureAwait(true);
            using var reader = new StreamReader(input);
            var envelope = (await reader.ReadToEndAsync(token).ConfigureAwait(true)).Trim();
            if (string.IsNullOrEmpty(envelope))
            {
                Tell(DataLabel("data.exporting_and_importing_a_config"));
                return;
            }

            restore.Children.Clear();
            restore.IsVisible = true;
            var phrase = new TextBox { PasswordChar = '●', PlaceholderText = DataLabel("data.passphrase") };
            restore.Children.Add(DataText(DataLabel("data.enter_the_passphrase_for_name", ("name", file.Name))));
            restore.Children.Add(phrase);
            var preview = DataButton(DataLabel("data.preview_differences"));
            restore.Children.Add(preview);
            preview.Click += async (_, _) => await Run(async () =>
            {
                var plan = await api.BackupFileAsync("preview", new JsonObject { ["file"] = envelope, ["passphrase"] = phrase.Text }, token).ConfigureAwait(true);
                phrase.Text = string.Empty;
                restore.Children.Clear();
                if ((string?)plan["planId"] is not { Length: > 0 } planId)
                {
                    Tell(DataLabel("common.failed"));
                    return;
                }

                var picks = new StackPanel();
                restore.Children.Add(picks);
                var consents = new StackPanel();
                restore.Children.Add(consents);
                foreach (var item in plan["items"]?.AsArray().OfType<JsonObject>() ?? [])
                {
                    var name = (string?)item["id"];
                    var state = (string?)item["status"];
                    var selected = new CheckBox { Content = (string?)item["name"] ?? name, Tag = name, IsChecked = state == "new" && (bool?)item["recommended"] == true, IsEnabled = state is not ("same" or "blocked" or "incompatible") };
                    ToolTip.SetTip(selected, (string?)item["summary"]);
                    picks.Children.Add(selected);
                    if ((string?)item["consent"] is { Length: > 0 } consent)
                    {
                        var allow = new CheckBox { Content = (string?)item["name"] + " · " + consent, Tag = name, IsVisible = selected.IsChecked == true };
                        selected.IsCheckedChanged += (_, _) =>
                        {
                            allow.IsVisible = selected.IsChecked == true;
                            allow.IsChecked = false;
                        };
                        consents.Children.Add(allow);
                    }
                }

                var apply = DataButton(DataLabel("usage.apply"));
                restore.Children.Add(apply);
                apply.Click += async (_, _) => await Run(async () =>
                {
                    var accepted = picks.Children.OfType<CheckBox>().Where(c => c.IsEnabled && c.IsChecked == true).Select(c => (string)c.Tag!).ToArray();
                    var acknowledged = consents.Children.OfType<CheckBox>().Where(c => c.IsVisible).ToArray();
                    if (accepted.Length == 0 || acknowledged.Any(c => c.IsChecked != true))
                    {
                        return;
                    }

                    var result = await api.BackupFileAsync("apply", new JsonObject { ["planId"] = planId, ["items"] = JsonSerializer.SerializeToNode(accepted), ["consented"] = JsonSerializer.SerializeToNode(acknowledged.Select(c => (string)c.Tag!)) }, token).ConfigureAwait(true);
                    restore.Children.Clear();
                    restore.IsVisible = false;
                    Tell(result.ToJsonString());
                }).ConfigureAwait(true);
                CancelRestore();
            }).ConfigureAwait(true);
            CancelRestore();
        }).ConfigureAwait(true);
        Validate();

        void CancelRestore()
        {
            var cancel = DataButton(DataLabel("common.cancel"));
            cancel.Click += (_, _) =>
            {
                restore.Children.Clear();
                restore.IsVisible = false;
            };
            restore.Children.Add(cancel);
        }

        static TextBox Password(Grid parent, int column, string title)
        {
            var stack = new StackPanel();
            var label = DataText(title, 12, 16.796875, true);
            label.Margin = new Thickness(0, 10, 0, 6);
            stack.Children.Add(label);
            var input = new TextBox { PasswordChar = '●', Height = 32, MinHeight = 0 };
            Avalonia.Automation.AutomationProperties.SetName(input, title);
            stack.Children.Add(input);
            DataColumn(parent, column, stack);
            return input;
        }

        string[] Selected() => chips.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag!).ToArray();

        void Tell(string text)
        {
            notice.Text = text;
            notice.IsVisible = true;
        }

        void Validate()
        {
            var n = Selected().Length;
            var length = pass.Text?.Length ?? 0;
            export.IsEnabled = n > 0 && length >= min && pass.Text == again.Text;
            extra.IsVisible = Selected().Contains("secrets");
            wait.IsVisible = !export.IsEnabled;
            wait.Text = DataLabel(n == 0 ? "data.tick_at_least_one_thing" : length < min ? "data.passphrase_too_short_n_more" : "data.the_two_passphrases_differ", ("n", (min - length).ToString(CultureInfo.CurrentCulture)));
        }

        async Task Run(Func<Task> work)
        {
            if (!form.IsEnabled)
            {
                return;
            }

            form.IsEnabled = false;
            try
            {
                await work().ConfigureAwait(true);
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
                form.IsEnabled = true;
            }
        }
    }
}
