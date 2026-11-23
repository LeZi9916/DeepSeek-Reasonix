using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Reviews a pasted external service and writes it only after confirmation.</summary>
internal sealed partial class ExtensionServiceSource : SettingsFormView
{
    /// <summary>Initializes a new instance of the <see cref="ExtensionServiceSource"/> class.</summary>
    public ExtensionServiceSource() => InitializeComponent();

    /// <summary>Connects the current workspace's read and install actions.</summary>
    /// <param name="api">Runtime settings client.</param>
    /// <param name="language">Settings language.</param>
    /// <param name="canProject">Whether the runtime has a project for the scoped actions.</param>
    /// <param name="describe">Localizes host refusals.</param>
    /// <param name="close">Closes the paste panel.</param>
    /// <param name="changed">Refreshes the inventory after a successful connection.</param>
    /// <param name="token">Current request lifetime.</param>
    internal void Open(
        StudioPageClient api,
        InterfaceLanguage language,
        bool canProject,
        Func<Exception, string> describe,
        Action close,
        Func<Task> changed,
        CancellationToken token)
    {
        Translate(language);
        User.Content = Text("ext.mine");
        Local.Content = Text("ext.this_project_only");
        Project.Content = Text("ext.commit_to_the_repo");
        ToolTip.SetTip(User, Text("ext.available_in_every_project"));
        ToolTip.SetTip(Local, Text("ext.stays_out_of_the_repository"));
        ToolTip.SetTip(Project, Text("ext.whoever_clones_it_gets_it"));
        AutomationProperties.SetName(Source, Hint.Text);
        JsonObject[] servers = [];
        var installed = new HashSet<string>(StringComparer.Ordinal);
        var busy = false;
        var done = false;
        Refresh();
        Source.TextChanged += (_, _) => Refresh();
        Project.IsCheckedChanged += (_, _) => Refresh();
        Source.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
            {
                e.Handled = true;
                await InspectAsync().ConfigureAwait(true);
            }
        };
        Cancel.Click += (_, _) =>
        {
            if (servers.Length == 0 || done)
            {
                close();
            }
            else if (!busy)
            {
                servers = [];
                installed.Clear();
                Status.IsVisible = false;
                Refresh();
            }
        };
        Inspect.Click += async (_, _) => await InspectAsync().ConfigureAwait(true);
        Connect.Click += async (_, _) => await ConnectAsync().ConfigureAwait(true);
        Source.Focus();

        void Refresh()
        {
            var pending = servers.Length > 0;
            Source.IsVisible = Inspect.IsVisible = Hint.IsVisible = !pending;
            Preview.IsVisible = pending;
            Scope.IsVisible = Connect.IsVisible = pending && !done;
            ProjectWarning.IsVisible = Project.IsChecked == true && Scope.IsVisible;
            Inspect.IsEnabled = !busy && !string.IsNullOrWhiteSpace(Source.Text);
            Source.IsEnabled = Connect.IsEnabled = Cancel.IsEnabled = !busy;
            User.IsEnabled = !busy && installed.Count == 0;
            Local.IsEnabled = Project.IsEnabled = User.IsEnabled && canProject;
            Cancel.Content = Text(done ? "common.done" : pending ? "common.back" : "common.cancel");
            Inspect.Content = Text(busy ? "ext.reading" : "ext.see_what_it_is");
            Connect.Content = Text(busy ? "ext.connecting" : "ext.connect");
        }

        async Task InspectAsync()
        {
            if (busy || Source.Text?.Trim() is not { Length: > 0 } input)
            {
                return;
            }

            await RunAsync(() => ReadPreviewAsync(input)).ConfigureAwait(true);
        }

        async Task ReadPreviewAsync(string input)
        {
            var draft = await api.InspectExtensionServiceAsync(input, token).ConfigureAwait(true);
            servers = draft["servers"]?.AsArray().OfType<JsonObject>().ToArray() ?? [];
            Preview.Children.Clear();
            foreach (var server in servers)
            {
                var identity = (string?)server["name"] ?? string.Empty;
                Preview.Children.Add(Caption(identity + " · " + (string?)server["transport"]));
                foreach (var risk in (draft["risks"]?.AsArray().OfType<JsonObject>() ?? []).Where(item => (string?)item["server"] == identity))
                {
                    var field = (string?)risk["field"] ?? string.Empty;
                    var displayed = (string?)risk["kind"] switch
                    {
                        "secret" => field.Split('.').LastOrDefault() + " · " + (string?)risk["detail"],
                        "unknown-host" => (string?)server["displayUrl"],
                        _ => string.Join(" ", new[] { (string?)server["displayCommand"] }.Concat(server["displayArgs"]?.AsArray().Select(item => (string?)item) ?? [])),
                    };
                    Preview.Children.Add(Caption(displayed ?? string.Empty));
                }
            }

            if (servers.Length == 0)
            {
                Status.Text = Text("ext.there_is_nothing_installable_at");
                Status.IsVisible = true;
            }
        }

        async Task ConnectAsync()
        {
            if (busy || servers.Length == 0 || done)
            {
                return;
            }

            var scope = Project.IsChecked == true ? "project" : Local.IsChecked == true ? "local" : "user";
            await RunAsync(ApplyAsync).ConfigureAwait(true);

            async Task ApplyAsync()
            {
                var saved = false;
                try
                {
                    foreach (var server in servers)
                    {
                        var identity = (string?)server["name"] ?? string.Empty;
                        if (installed.Contains(identity))
                        {
                            continue;
                        }

                        var result = await api.InstallExtensionServiceAsync(server, scope, token).ConfigureAwait(true);
                        if ((string?)result["state"] != "issue")
                        {
                            saved = true;
                            installed.Add(identity);
                        }

                        Preview.Children.Add(Caption(identity + " · " + ((string?)result["error"] ?? (string?)result["state"])));
                    }

                    done = installed.Count == servers.Length;
                }
                finally
                {
                    if (saved)
                    {
                        await changed().ConfigureAwait(true);
                    }
                }
            }
        }

        async Task RunAsync(Func<Task> action)
        {
            Status.IsVisible = false;
            busy = true;
            Refresh();
            try
            {
                await action().ConfigureAwait(true);
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    Status.Text = describe(error);
                    Status.IsVisible = true;
                }
            }
            finally
            {
                busy = false;
                Refresh();
            }
        }
    }
}
