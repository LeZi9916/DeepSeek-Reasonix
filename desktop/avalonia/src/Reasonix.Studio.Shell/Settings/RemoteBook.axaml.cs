using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>The local hub's saved SSH machines and WSL distributions.</summary>
public sealed partial class RemoteBook : SettingsFormView
{
    /// <summary>Compares the current form to its opened version.</summary>
    private Func<bool>? _dirty;

    /// <summary>Initializes a new instance of the <see cref="RemoteBook"/> class.</summary>
    public RemoteBook() => InitializeComponent();

    /// <summary>Gets a value indicating whether the form has unsaved edits.</summary>
    internal bool HasChanges => _dirty?.Invoke() == true;

    /// <summary>Connects an address book to its host and local discovery results.</summary>
    /// <param name="api">The hub.</param>
    /// <param name="hosts">Initial saved entries.</param>
    /// <param name="aliases">Local SSH aliases.</param>
    /// <param name="distros">Local WSL inventory.</param>
    /// <param name="language">Shell language.</param>
    /// <param name="describe">Localizes refusals.</param>
    /// <param name="rail">Device-local sidebar visibility.</param>
    /// <param name="browse">Opens the saved machine's directory picker.</param>
    /// <param name="token">Window lifetime.</param>
    internal void Open(
        StudioPageClient api,
        JsonArray hosts,
        JsonArray aliases,
        JsonObject distros,
        InterfaceLanguage language,
        Func<Exception, string> describe,
        (Func<string, bool> IsHidden, Func<string, Task> Toggle) rail,
        Func<string, string, Task<string?>> browse,
        CancellationToken token)
    {
        Translate(language);
        JsonObject? initial = null;
        var probeResults = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var workspaces = new List<string>();
        string? editingName = null;
        var disabled = false;
        _dirty = () => Editor.IsVisible && !JsonNode.DeepEquals(initial, Draft());
        var distributions = (distros["distros"]?.AsArray().OfType<JsonObject>() ?? []).Where(d => (int?)d["version"] != 1).Select(d => (string?)d["name"]).OfType<string>().ToArray();
        Provider.ItemsSource = new[] { new Option("local", Text("remote.this_machines_over_the_tunnel")), new Option("remote", Text("remote.that_machines_own")) };
        Installer.ItemsSource = new[] { new Option("auto", Text("common.pick_one_automatically")), new Option("npm", Text("remote.use_the_remotes_npm")), new Option("upload", Text("remote.upload_this_machines")), new Option("never", Text("remote.do_not_install_there_is")) };
        MachineName.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                Validate();
            }
        };
        Distro.SelectionChanged += (_, _) => Validate();
        Add.Click += (_, _) => EditHost(null);
        SshChoice.IsCheckedChanged += (_, _) => ShowAddressFields();
        WslChoice.IsCheckedChanged += (_, _) => ShowAddressFields();
        Port.TextChanged += (_, _) =>
        {
            var value = Port.Text ?? string.Empty;
            var digits = new string(value.Where(char.IsAsciiDigit).ToArray());
            if (value != digits)
            {
                Port.Text = digits;
            }
        };
        DirectoryDraft.TextChanged += (_, _) => AddDirectory.IsEnabled = !string.IsNullOrWhiteSpace(DirectoryDraft.Text);
        AddDirectory.Click += (_, _) => AddFolder();
        Browse.Click += async (_, _) =>
        {
            if (editingName is { } host && await browse(host, workspaces.FirstOrDefault() ?? string.Empty).ConfigureAwait(true) is { } path)
            {
                AddPath(path);
            }
        };
        DirectoryDraft.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                AddFolder();
                e.Handled = true;
            }
        };
        Cancel.Click += (_, _) => CloseForm();
        Save.Click += async (_, _) => await RunAsync(async () =>
        {
            var edit = Draft();
            await api.SaveRemoteAsync(edit, token).ConfigureAwait(true);
            CloseForm();
            await ReloadAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);
        Render();

        JsonObject Draft() => new()
        {
            ["name"] = MachineName.Text?.Trim(),
            ["kind"] = WslChoice.IsChecked == true ? "wsl" : "ssh",
            ["distro"] = Distro.SelectedItem as string ?? string.Empty,
            ["host"] = Host.Text?.Trim(),
            ["user"] = User.Text?.Trim(),
            ["port"] = string.IsNullOrEmpty(Port.Text)
                ? 0
                : int.TryParse(Port.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) ? port : int.MaxValue,
            ["identityFile"] = Identity.Text,
            ["proxyJump"] = Jump.Text,
            ["passphraseEnv"] = Passphrase.Text,
            ["passwordEnv"] = PasswordEnv.Text,
            ["useSSHConfig"] = SshConfig.IsChecked == true,
            ["disabled"] = disabled,
            ["provider"] = ((Option?)Provider.SelectedItem)?.Id,
            ["serveInstall"] = ((Option?)Installer.SelectedItem)?.Id,
            ["workspaces"] = JsonSerializer.SerializeToNode(workspaces),
        };

        void EditHost(JsonObject? h)
        {
            Result.IsVisible = false;
            Editor.IsVisible = true;
            Add.IsVisible = false;
            Empty.IsVisible = false;
            editingName = (string?)h?["name"];
            Browse.IsVisible = editingName is not null;
            MachineName.Text = (string?)h?["name"] ?? string.Empty;
            MachineName.IsReadOnly = h is not null;
            KindField.IsVisible = h is null && (bool?)distros["available"] == true;
            var savedDistro = (string?)h?["distro"];
            Distro.ItemsSource = savedDistro is { Length: > 0 } && !distributions.Contains(savedDistro, StringComparer.Ordinal)
                ? distributions.Prepend(savedDistro).ToArray()
                : distributions;
            Distro.SelectedItem = savedDistro;
            Distro.IsEnabled = h is null;
            ((string?)h?["kind"] == "wsl" ? WslChoice : SshChoice).IsChecked = true;
            Host.Text = (string?)h?["host"] ?? string.Empty;
            Port.Text = (int?)h?["port"] is > 0 and var port ? port.ToString(CultureInfo.InvariantCulture) : string.Empty;
            User.Text = (string?)h?["user"] ?? string.Empty;
            Identity.Text = (string?)h?["identityFile"] ?? string.Empty;
            Jump.Text = (string?)h?["proxyJump"] ?? string.Empty;
            Passphrase.Text = (string?)h?["passphraseEnv"] ?? string.Empty;
            PasswordEnv.Text = (string?)h?["passwordEnv"] ?? string.Empty;
            disabled = (bool?)h?["disabled"] == true;
            SshConfig.IsChecked = (bool?)h?["useSSHConfig"] == true;
            SetChoice(Provider, (string?)h?["provider"] ?? "local");
            SetChoice(Installer, (string?)h?["serveInstall"] ?? "auto");
            workspaces.Clear();
            if (h?["workspaces"] is JsonArray { Count: > 0 } folders)
            {
                workspaces.AddRange(folders.Select(folder => (string?)folder).OfType<string>().Distinct(StringComparer.Ordinal));
            }
            else if ((string?)h?["workspace"] is { Length: > 0 } workspace)
            {
                workspaces.Add(workspace);
            }

            DirectoryDraft.Text = string.Empty;
            AddDirectory.IsEnabled = false;
            Save.Content = Text(h is null ? "common.add" : "common.save");
            RenderFolders();
            ShowAddressFields();
            initial = Draft();
            MachineName.Focus();
        }

        void ShowAddressFields()
        {
            var wsl = WslChoice.IsChecked == true;
            AddressFields.IsVisible = AuthenticationFields.IsVisible = ProviderField.IsVisible = SshConfig.IsVisible = !wsl;
            DistroField.IsVisible = WslHint.IsVisible = wsl;
            if (wsl && Distro.IsEnabled && Distro.SelectedIndex < 0 && Distro.ItemCount > 0)
            {
                Distro.SelectedIndex = 0;
            }

            Validate();
        }

        void Validate() => Save.IsEnabled = !string.IsNullOrWhiteSpace(MachineName.Text) && (WslChoice.IsChecked != true || Distro.SelectedItem is string);

        void AddFolder()
        {
            var path = DirectoryDraft.Text?.Trim();
            if (!string.IsNullOrEmpty(path))
            {
                AddPath(path);
            }

            DirectoryDraft.Text = string.Empty;
        }

        void AddPath(string path)
        {
            if (!workspaces.Contains(path, StringComparer.Ordinal))
            {
                workspaces.Add(path);
                RenderFolders();
            }
        }

        void RenderFolders()
        {
            Folders.Children.Clear();
            foreach (var path in workspaces)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 10 };
                row.Children.Add(new Border
                {
                    Padding = new Thickness(8, 4),
                    CornerRadius = new CornerRadius(5),
                    [!Border.BackgroundProperty] = StudioTypography.Resource("OverlayBrush"),
                    Child = new TextBlock
                    {
                        Text = path,
                        TextWrapping = TextWrapping.Wrap,
                        [!TextBlock.FontFamilyProperty] = StudioTypography.Resource("StudioMonoFamily"),
                        [!TextBlock.FontSizeProperty] = StudioTypography.Size(12),
                    },
                });
                if (path == workspaces[0])
                {
                    var label = new TextBlock
                    {
                        Text = Text("common.default"),
                        VerticalAlignment = VerticalAlignment.Center,
                        [!TextBlock.ForegroundProperty] = StudioTypography.Resource("AccentInkBrush"),
                        [!TextBlock.FontSizeProperty] = StudioTypography.Size(11),
                    };
                    var badge = new Border
                    {
                        Height = 18,
                        CornerRadius = new CornerRadius(9),
                        Padding = new Thickness(6, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        [!Border.BackgroundProperty] = StudioTypography.Resource("AccentWashBrush"),
                        Child = label,
                    };
                    Grid.SetColumn(badge, 1);
                    row.Children.Add(badge);
                }
                else
                {
                    var makeDefault = new Button { Content = Text("common.make_default"), Classes = { "directory-action", "ghost" } };
                    AutomationProperties.SetName(makeDefault, Text("remote.make_path_the_default", ("path", path)));
                    AutomationProperties.SetAutomationId(makeDefault, "action:remote.directory.default");
                    makeDefault.Click += (_, _) =>
                    {
                        workspaces.Remove(path);
                        workspaces.Insert(0, path);
                        RenderFolders();
                    };
                    Grid.SetColumn(makeDefault, 1);
                    row.Children.Add(makeDefault);
                }

                var remove = new Button { Content = Text("common.delete"), Classes = { "directory-action", "ghost" } };
                AutomationProperties.SetName(remove, Text("remote.stop_listing_path", ("path", path)));
                AutomationProperties.SetAutomationId(remove, "action:remote.directory.remove");
                remove.Click += (_, _) =>
                {
                    workspaces.Remove(path);
                    RenderFolders();
                };
                Grid.SetColumn(remove, 2);
                row.Children.Add(remove);
                Folders.Children.Add(row);
            }
        }

        void CloseForm()
        {
            Editor.IsVisible = false;
            Add.IsVisible = true;
            Empty.IsVisible = Rows.Children.Count == 0;
            initial = null;
            Add.Focus();
        }

        async Task ReloadAsync()
        {
            hosts = await api.RemoteHostsAsync(token).ConfigureAwait(true) ?? [];
            Render();
        }

        async Task RunAsync(Func<Task> action)
        {
            if (!IsEnabled)
            {
                return;
            }

            IsEnabled = false;
            Result.IsVisible = false;
            try
            {
                await action().ConfigureAwait(true);
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    Result.Text = describe(error);
                    Result.IsVisible = true;
                }
            }
            finally
            {
                IsEnabled = true;
            }
        }

        void Render()
        {
            Rows.Children.Clear();
            foreach (var h in hosts.OfType<JsonObject>())
            {
                var name = (string)h["name"]!;
                var row = new RemoteMachineCard();
                row.Show(h, language, rail.IsHidden(name));
                if (probeResults.TryGetValue(name, out var previous))
                {
                    ShowProbe(row, name, previous);
                }

                row.EditCommand.Click += (_, _) =>
                {
                    row.Actions.Hide();
                    EditHost(h);
                };
                row.EnableCommand.Click += async (_, _) =>
                {
                    row.Actions.Hide();
                    await RunAsync(async () =>
                    {
                        var entry = (JsonObject)h.DeepClone();
                        entry["disabled"] = (bool?)h["disabled"] != true;
                        await api.SaveRemoteAsync(entry, token).ConfigureAwait(true);
                        await ReloadAsync().ConfigureAwait(true);
                    }).ConfigureAwait(true);
                };
                row.RailCommand.Click += async (_, _) =>
                {
                    row.Actions.Hide();
                    await RunAsync(async () =>
                    {
                        await rail.Toggle(name).ConfigureAwait(true);
                        row.RailCommand.Header = Text(rail.IsHidden(name) ? "remote.show_in_the_rail" : "remote.hide_from_the_rail");
                    }).ConfigureAwait(true);
                };
                row.RemoveCommand.Click += (_, _) =>
                {
                    row.Actions.Hide();
                    foreach (var other in Rows.Children.OfType<RemoteMachineCard>())
                    {
                        other.AskRemoval(false);
                    }

                    row.AskRemoval(true);
                };
                row.Remove.Click += async (_, _) => await RunAsync(async () =>
                {
                    await api.RemoveRemoteAsync(name, token).ConfigureAwait(true);
                    probeResults.Remove(name);
                    await ReloadAsync().ConfigureAwait(true);
                }).ConfigureAwait(true);
                row.Probe.Click += async (_, _) => await ProbeAsync().ConfigureAwait(true);
                row.ProbeCommand.Click += async (_, _) =>
                {
                    row.Actions.Hide();
                    await ProbeAsync().ConfigureAwait(true);
                };
                Rows.Children.Add(row);

                Task ProbeAsync() => RunAsync(async () =>
                {
                    row.Probe.Content = row.ProbeCommand.Header = Text("remote.asking");
                    try
                    {
                        var answer = await api.ProbeRemoteAsync(name, token).ConfigureAwait(true);
                        probeResults[name] = answer;
                        ShowProbe(row, name, answer);
                    }
                    finally
                    {
                        row.Probe.Content = row.ProbeCommand.Header = Text("common.test_it");
                    }
                });
            }

            Empty.IsVisible = Rows.Children.Count == 0 && !Editor.IsVisible;
            RenderCandidates();
        }

        void RenderCandidates()
        {
            var saved = hosts.OfType<JsonObject>().Select(h => (string?)h["name"]).ToHashSet(StringComparer.Ordinal);
            var available = aliases.OfType<JsonValue>().Select(a => a.GetValue<string>()).Where(a => !saved.Contains(a)).ToArray();
            Candidates.Children.Clear();
            Candidates.Children.Add(CandidateLabel);
            Candidates.IsVisible = available.Length > 0;
            foreach (var alias in available)
            {
                var chip = new Button { Content = alias, Classes = { "remote-candidate", "alias" } };
                ToolTip.SetTip(chip, Text("remote.add_it_with_the_settings"));
                AutomationProperties.SetAutomationId(chip, "action:remotes.add");
                chip.Click += async (_, _) => await ImportAsync([alias]).ConfigureAwait(true);
                Candidates.Children.Add(chip);
            }

            if (available.Length > 1)
            {
                var all = new Button { Content = Text("remote.add_them_all"), Classes = { "remote-candidate" } };
                AutomationProperties.SetAutomationId(all, "action:remotes.add-all");
                all.Click += async (_, _) => await ImportAsync(available).ConfigureAwait(true);
                Candidates.Children.Add(all);
            }
        }

        Task ImportAsync(string[] aliasesToSave) => RunAsync(async () =>
        {
            try
            {
                foreach (var alias in aliasesToSave)
                {
                    await api.SaveRemoteAsync(new JsonObject { ["name"] = alias, ["useSSHConfig"] = true }, token).ConfigureAwait(true);
                }
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    await ReloadAsync().ConfigureAwait(true);
                }
            }
        });

        void ShowProbe(RemoteMachineCard row, string name, JsonObject answer)
        {
            var ready = (bool?)answer["ready"] == true;
            var summary = string.Join(" · ", new[] { name, (string?)answer["os"], (string?)answer["arch"], (string?)answer["version"], Text(ready ? "remote.ready_there_is_a_kernel" : "remote.not_ready_clearing_any_one") }.Where(v => !string.IsNullOrEmpty(v)));
            var routes = (answer["routes"]?.AsArray().OfType<JsonObject>() ?? []).Select(r => string.Join(" · ", new[] { (string?)r["name"], (string?)r["detail"], (string?)r["error"] }.Where(v => !string.IsNullOrEmpty(v))));
            row.ShowProbe(string.Join(Environment.NewLine, routes.Prepend(summary)), ready);
        }
    }

    /// <summary>Reselects a stored setting by its stable id.</summary>
    /// <param name="box">The available choices.</param>
    /// <param name="id">Saved identifier.</param>
    private static void SetChoice(ComboBox box, string id) => box.SelectedItem = box.Items.OfType<Option>().FirstOrDefault(o => o.Id == id) ?? box.Items[0];
}
