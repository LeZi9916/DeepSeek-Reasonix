using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Reviews a package source before a separate, explicit install action.</summary>
internal sealed partial class PluginSourceView : SettingsFormView
{
    /// <summary>Initializes a new instance of the <see cref="PluginSourceView"/> class.</summary>
    public PluginSourceView() => InitializeComponent();

    /// <summary>Binds the source form to the host and leaves inspection separate from installation.</summary>
    /// <param name="api">Current settings host.</param>
    /// <param name="language">Visible language.</param>
    /// <param name="package">The installed package being updated, if one was selected.</param>
    /// <param name="pickFolder">Opens the folder selector first for the import action.</param>
    /// <param name="describe">Failure formatter.</param>
    /// <param name="cancel">Closes the form.</param>
    /// <param name="changed">Reloads installed data after acknowledgement.</param>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The optional initial inspection or folder picker.</returns>
    internal async Task OpenAsync(
        StudioPageClient api,
        InterfaceLanguage language,
        JsonObject? package,
        bool pickFolder,
        Func<Exception, string> describe,
        Action cancel,
        Func<Task> changed,
        CancellationToken token)
    {
        Translate(language);
        AutomationProperties.SetName(Source, Hint.Text);
        Install.Content = Text(package is null ? "ext.install" : "ext.update");
        Source.Text = (string?)package?["source"];
        var busy = false;
        var applied = false;
        JsonObject? reviewed = null;
        string? reviewedSource = null;

        Refresh();
        Source.TextChanged += (_, _) => Refresh();
        Source.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
            {
                e.Handled = true;
                await InspectAsync().ConfigureAwait(true);
            }
        };
        Folder.Click += async (_, _) => await PickAsync().ConfigureAwait(true);
        Cancel.Click += (_, _) =>
        {
            if (!busy)
            {
                cancel();
            }
        };
        Inspect.Click += async (_, _) => await InspectAsync().ConfigureAwait(true);
        Install.Click += async (_, _) =>
        {
            if (reviewed is not { } plan || reviewedSource is null || busy || applied)
            {
                return;
            }

            await RunAsync(async () =>
            {
                var result = await api.PluginPlanAsync(true, Request(reviewedSource, (string?)plan["planId"]), token).ConfigureAwait(true);
                applied = (bool?)result["applied"] == true;
                ShowResult(result);
                if (applied)
                {
                    await changed().ConfigureAwait(true);
                }
            }).ConfigureAwait(true);
        };
        Source.Focus();
        if (pickFolder)
        {
            await PickAsync().ConfigureAwait(true);
        }
        else if (package is not null && !string.IsNullOrWhiteSpace(Source.Text))
        {
            await InspectAsync().ConfigureAwait(true);
        }

        JsonObject Request(string source, string? id)
        {
            var payload = new JsonObject { ["source"] = source, ["replace"] = package is not null };
            if (package is not null)
            {
                payload["name"] = (string?)package["name"];
            }

            if (id is not null)
            {
                payload["planId"] = id;
            }

            return payload;
        }

        void Refresh()
        {
            Source.IsVisible = Folder.IsVisible = Hint.IsVisible = Inspect.IsVisible = reviewed is null && !applied;
            PlanItems.IsVisible = reviewed is not null || applied;
            Install.IsVisible = reviewed is not null && !applied;
            Source.IsEnabled = Folder.IsEnabled = Cancel.IsEnabled = Install.IsEnabled = !busy;
            Inspect.IsEnabled = !busy && !string.IsNullOrWhiteSpace(Source.Text);
            Inspect.Content = Text(busy ? "ext.reading" : "ext.see_what_it_is");
            Cancel.Content = Text(applied ? "common.done" : "common.cancel");
        }

        async Task RunAsync(Func<Task> action)
        {
            busy = true;
            Status.IsVisible = false;
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

        Task InspectAsync()
        {
            if (busy || Source.Text?.Trim() is not { Length: > 0 } source)
            {
                return Task.CompletedTask;
            }

            return RunAsync(async () =>
            {
                var plan = await api.PluginPlanAsync(false, Request(source, null), token).ConfigureAwait(true);
                if (plan["actions"] is not JsonArray { Count: > 0 })
                {
                    Status.Text = (string?)plan["error"] ?? (string?)plan["next"] ?? Text("ext.there_is_nothing_installable_at");
                    Status.IsVisible = true;
                    return;
                }

                reviewed = plan;
                reviewedSource = source;
                ShowResult(plan);
            });
        }

        async Task PickAsync()
        {
            if (!busy && TopLevel.GetTopLevel(this)?.StorageProvider is { CanPickFolder: true } storage)
            {
                var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Text("common.choose_a_folder") }).ConfigureAwait(true);
                if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path)
                {
                    Source.Text = path;
                }
            }
        }

        void ShowResult(JsonObject result)
        {
            PlanItems.Children.Clear();
            var actions = result["actions"]?.AsArray().OfType<JsonObject>() ?? [];
            foreach (var risk in new[] { "high", "medium", "low" })
            {
                var group = actions.Where(item => ((string?)item["riskLevel"] ?? "low") == risk).ToArray();
                if (group.Length == 0)
                {
                    continue;
                }

                var title = risk switch
                {
                    "high" => "ext.these_run_programs_on_this",
                    "medium" => "ext.these_change_what_is_available",
                    _ => "ext.these_only_add_files",
                };
                PlanItems.Children.Add(new TextBlock { Text = Text(title), FontSize = 13, FontWeight = Avalonia.Media.FontWeight.SemiBold, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                foreach (var item in group)
                {
                    PlanItems.Children.Add(Caption(string.Join(" · ", new[] { (string?)item["kind"], (string?)item["name"], (string?)item["description"], (string?)item["command"], (string?)item["target"] }.Where(value => !string.IsNullOrWhiteSpace(value)))));
                }
            }

            var warnings = result["warnings"]?.AsArray().Select(item => (string?)item) ?? [];
            Status.Text = string.Join("\n", warnings.Prepend((string?)result["error"]).Where(value => !string.IsNullOrWhiteSpace(value)));
            Status.IsVisible = !string.IsNullOrEmpty(Status.Text);
        }
    }
}
