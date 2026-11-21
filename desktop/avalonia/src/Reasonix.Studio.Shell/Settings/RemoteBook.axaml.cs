using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
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
    /// <param name="token">Window lifetime.</param>
    internal void Open(StudioPageClient api, JsonArray hosts, JsonArray aliases, JsonObject distros, InterfaceLanguage language, Func<Exception, string> describe, CancellationToken token)
    {
        Translate(language);
        JsonObject? initial = null;
        _dirty = () => Editor.IsVisible && !JsonNode.DeepEquals(initial, Draft());
        var ssh = new Option("ssh", Text("remote.ssh_host"));
        var wsl = new Option("wsl", Text("remote.wsl_distribution"));
        Distro.ItemsSource = (distros["distros"]?.AsArray().OfType<JsonObject>() ?? []).Where(d => (int?)d["version"] != 1).Select(d => (string?)d["name"]).OfType<string>().ToArray();
        Provider.ItemsSource = new[] { new Option(string.Empty, Text("common.default")), new Option("local", Text("remote.this_machines_over_the_tunnel")), new Option("remote", Text("remote.that_machines_own")) };
        Installer.ItemsSource = new[] { new Option("auto", Text("common.pick_one_automatically")), new Option("npm", Text("remote.use_the_remotes_npm")), new Option("upload", Text("remote.upload_this_machines")), new Option("never", Text("remote.do_not_install_there_is")) };
        Aliases.ItemsSource = aliases.Select(a => a?.ToString()).OfType<string>().ToArray();
        Aliases.PlaceholderText = Text("remote.also_in_ssh_config");
        Aliases.IsVisible = Aliases.ItemCount > 0;
        MachineName.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                Validate();
            }
        };
        Distro.SelectionChanged += (_, _) => Validate();
        Add.Click += (_, _) => EditHost(null);
        Aliases.SelectionChanged += (_, _) =>
        {
            if (Aliases.SelectedItem is string alias)
            {
                EditHost(new JsonObject { ["name"] = alias, ["useSSHConfig"] = true }, true);
                Aliases.SelectedIndex = -1;
            }
        };
        Kind.SelectionChanged += (_, _) =>
        {
            Ssh.IsVisible = !Equals(Kind.SelectedItem, wsl);
            Distro.IsVisible = !Ssh.IsVisible;
            Validate();
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
            ["kind"] = ((Option?)Kind.SelectedItem)?.Id,
            ["distro"] = Distro.SelectedItem as string ?? string.Empty,
            ["host"] = Host.Text?.Trim(),
            ["user"] = User.Text?.Trim(),
            ["port"] = (int)(Port.Value ?? 0),
            ["identityFile"] = Identity.Text,
            ["proxyJump"] = Jump.Text,
            ["passphraseEnv"] = Passphrase.Text,
            ["passwordEnv"] = PasswordEnv.Text,
            ["useSSHConfig"] = SshConfig.IsChecked == true,
            ["disabled"] = Disabled.IsChecked == true,
            ["provider"] = ((Option?)Provider.SelectedItem)?.Id,
            ["serveInstall"] = ((Option?)Installer.SelectedItem)?.Id,
            ["workspaces"] = JsonSerializer.SerializeToNode((Paths.Text ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct()),
        };

        void EditHost(JsonObject? h, bool adding = false)
        {
            Result.IsVisible = false;
            Editor.IsVisible = true;
            List.IsVisible = false;
            MachineName.Text = (string?)h?["name"] ?? string.Empty;
            MachineName.IsReadOnly = h is not null && !adding;
            Kind.ItemsSource = (bool?)distros["available"] == true || (string?)h?["kind"] == "wsl" ? new[] { ssh, wsl } : new[] { ssh };
            Kind.SelectedItem = (string?)h?["kind"] == "wsl" ? wsl : ssh;
            Kind.IsVisible = Kind.ItemCount > 1;
            Distro.SelectedItem = (string?)h?["distro"];
            Host.Text = (string?)h?["host"] ?? string.Empty;
            Port.Value = (int?)h?["port"] ?? 0;
            User.Text = (string?)h?["user"] ?? string.Empty;
            Identity.Text = (string?)h?["identityFile"] ?? string.Empty;
            Jump.Text = (string?)h?["proxyJump"] ?? string.Empty;
            Passphrase.Text = (string?)h?["passphraseEnv"] ?? string.Empty;
            PasswordEnv.Text = (string?)h?["passwordEnv"] ?? string.Empty;
            Disabled.IsChecked = (bool?)h?["disabled"] == true;
            SshConfig.IsChecked = (bool?)h?["useSSHConfig"] == true;
            SetChoice(Provider, (string?)h?["provider"] ?? string.Empty);
            SetChoice(Installer, (string?)h?["serveInstall"] ?? "auto");
            Paths.Text = h?["workspaces"] is JsonArray { Count: > 0 } folders ? string.Join(Environment.NewLine, folders.Select(f => (string?)f)) : (string?)h?["workspace"] ?? string.Empty;
            initial = Draft();
            Validate();
            MachineName.Focus();
        }

        void Validate() => Save.IsEnabled = !string.IsNullOrWhiteSpace(MachineName.Text) && (!Equals(Kind.SelectedItem, wsl) || Distro.SelectedItem is string);

        void CloseForm()
        {
            Editor.IsVisible = false;
            List.IsVisible = true;
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
                var row = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4) };
                row.Children.Add(new TextBlock { Text = name + "   " + ((string?)h["host"] ?? (string?)h["distro"]), FontSize = 14 });
                var actions = new WrapPanel();
                var edit = new Button { Content = Text("common.edit") };
                var probe = new Button { Content = Text("common.test_it"), IsEnabled = (bool?)h["disabled"] != true && (string?)h["status"] != "connected" };
                var remove = new Button { Content = Text("common.remove") };
                var confirm = new Button { Content = Text("common.delete"), IsVisible = false };
                var keep = new Button { Content = Text("common.cancel"), IsVisible = false };
                foreach (var (button, id) in new[] { (edit, "edit"), (probe, "probe"), (remove, "remove"), (confirm, "confirm-remove"), (keep, "cancel-remove") })
                {
                    button.Margin = new Thickness(0, 0, 5, 0);
                    AutomationProperties.SetAutomationId(button, "action:remote." + id);
                    actions.Children.Add(button);
                }

                edit.Click += (_, _) => EditHost(h);
                remove.Click += (_, _) => confirm.IsVisible = keep.IsVisible = true;
                keep.Click += (_, _) => confirm.IsVisible = keep.IsVisible = false;
                confirm.Click += async (_, _) => await RunAsync(async () =>
                {
                    await api.RemoveRemoteAsync(name, token).ConfigureAwait(true);
                    await ReloadAsync().ConfigureAwait(true);
                }).ConfigureAwait(true);
                probe.Click += async (_, _) => await RunAsync(async () =>
                {
                    var answer = await api.ProbeRemoteAsync(name, token).ConfigureAwait(true);
                    var summary = string.Join(" · ", new[] { name, (string?)answer["os"], (string?)answer["arch"], (string?)answer["version"], Text((bool?)answer["ready"] == true ? "remote.ready_there_is_a_kernel" : "remote.not_ready_clearing_any_one") }.Where(v => !string.IsNullOrEmpty(v)));
                    var routes = (answer["routes"]?.AsArray().OfType<JsonObject>() ?? []).Select(r => string.Join(" · ", new[] { (string?)r["name"], (string?)r["detail"], (string?)r["error"] }.Where(v => !string.IsNullOrEmpty(v))));
                    Result.Text = string.Join(Environment.NewLine, routes.Prepend(summary));
                    Result.IsVisible = true;
                }).ConfigureAwait(true);
                row.Children.Add(actions);
                Rows.Children.Add(row);
            }
        }
    }

    /// <summary>Reselects a stored setting by its stable id.</summary>
    /// <param name="box">The available choices.</param>
    /// <param name="id">Saved identifier.</param>
    private static void SetChoice(ComboBox box, string id) => box.SelectedItem = box.Items.OfType<Option>().FirstOrDefault(o => o.Id == id) ?? box.Items[0];
}
