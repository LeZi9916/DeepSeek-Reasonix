using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Controls;
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

/// <summary>The writable roots and effective command confinement shown by the security settings.</summary>
public sealed partial class SafetySandboxView : SettingsFormView
{
    /// <summary>Initializes a new instance of the <see cref="SafetySandboxView"/> class.</summary>
    public SafetySandboxView() => InitializeComponent();

    /// <summary>Loads the machine policy and the project-specific folder grants.</summary>
    /// <param name="api">The active host endpoints.</param>
    /// <param name="language">The interface language.</param>
    /// <param name="pick">Opens the host folder chooser.</param>
    /// <param name="describe">Localizes a failure.</param>
    /// <param name="token">The window lifetime.</param>
    /// <returns>The first two reads.</returns>
    internal async Task OpenAsync(StudioPageClient api, InterfaceLanguage language, Func<string, Task<string?>> pick, Func<Exception, string> describe, CancellationToken token)
    {
        Translate(language);
        Confined.Content = Text("safety.jailed");
        Unconfined.Content = Text("safety.unconfined");
        Confined.GroupName = Unconfined.GroupName = Guid.NewGuid().ToString("N");
        Confined.Tag = "enforce";
        Unconfined.Tag = "off";
        AutomationProperties.SetName(ProjectPath, Text("session.folder_path_to_add"));
        var settings = new JsonObject();
        var folders = new JsonObject();
        var busy = false;
        var rendering = false;
        WorkspaceRoot.LostFocus += async (_, _) => await SaveRootAsync().ConfigureAwait(true);
        WorkspaceRoot.KeyDown += async (_, e) => await EnterAsync(e, SaveRootAsync).ConfigureAwait(true);
        ChooseRoot.Click += async (_, _) =>
        {
            if (await pick(WorkspaceRoot.Text ?? string.Empty).ConfigureAwait(true) is { Length: > 0 } path)
            {
                await SaveAsync(edit => edit["workspaceRoot"] = path).ConfigureAwait(true);
            }
        };
        ExtraRoot.TextChanged += (_, _) => AddRoot.IsEnabled = !string.IsNullOrWhiteSpace(ExtraRoot.Text);
        ProjectPath.TextChanged += (_, _) => AddProject.IsEnabled = !string.IsNullOrWhiteSpace(ProjectPath.Text);
        AddRoot.Click += async (_, _) => await AddRootAsync().ConfigureAwait(true);
        ExtraRoot.KeyDown += async (_, e) => await EnterAsync(e, AddRootAsync).ConfigureAwait(true);
        Protect.IsCheckedChanged += async (_, _) => await ChangeAsync("protectChangedFiles", Protect.IsChecked == true).ConfigureAwait(true);
        Network.IsCheckedChanged += async (_, _) => await ChangeAsync("network", Network.IsChecked == true).ConfigureAwait(true);
        Confined.IsCheckedChanged += async (_, _) => await SelectModeAsync(Confined).ConfigureAwait(true);
        Unconfined.IsCheckedChanged += async (_, _) => await SelectModeAsync(Unconfined).ConfigureAwait(true);

        AddProject.Click += async (_, _) => await AddProjectAsync().ConfigureAwait(true);
        ProjectPath.KeyDown += async (_, e) => await EnterAsync(e, AddProjectAsync).ConfigureAwait(true);
        PickProject.Click += async (_, _) =>
        {
            if (await pick(PrimaryFolder.Text ?? string.Empty).ConfigureAwait(true) is { Length: > 0 } path)
            {
                ProjectPath.Text = path;
            }
        };
        RetryFolders.Click += async (_, _) => await ReadFoldersAsync().ConfigureAwait(true);
        await Task.WhenAll(ReadSandboxAsync(), ReadFoldersAsync()).ConfigureAwait(true);

        async Task ReadSandboxAsync()
        {
            try
            {
                settings = await api.SandboxAsync(token).ConfigureAwait(true);
                Controls.IsVisible = true;
                RenderSandbox();
            }
            catch (Exception)
            {
                LoadIssue.IsVisible = !token.IsCancellationRequested;
            }
        }

        async Task ReadFoldersAsync()
        {
            try
            {
                folders = await api.WorkspaceRootsAsync(token).ConfigureAwait(true);
                RenderFolders();
                RetryFolders.IsVisible = FolderError.IsVisible = false;
            }
            catch (Exception)
            {
                RetryFolders.IsVisible = FolderError.IsVisible = !token.IsCancellationRequested;
                FolderError.Text = Text("session.could_not_read_this_projects");
            }
        }

        Task SaveRootAsync()
        {
            var value = WorkspaceRoot.Text ?? string.Empty;
            return value == (string?)settings["workspaceRoot"] ? Task.CompletedTask : SaveAsync(edit => edit["workspaceRoot"] = value);
        }

        async Task AddRootAsync()
        {
            if (ExtraRoot.Text?.Trim() is { Length: > 0 } path &&
                await TrySaveAsync(edit => edit["allowWrite"] = Strings(Strings(edit["allowWrite"]).Append(path))).ConfigureAwait(true))
            {
                ExtraRoot.Text = string.Empty;
            }
        }

        async Task AddProjectAsync()
        {
            if (ProjectPath.Text?.Trim() is { Length: > 0 } path && await ChangeFolderAsync(path, true).ConfigureAwait(true))
            {
                ProjectPath.Text = string.Empty;
            }
        }

        Task ChangeAsync(string key, bool value) => rendering ? Task.CompletedTask : SaveAsync(edit => edit[key] = value);

        Task SelectModeAsync(RadioButton button) =>
            rendering || button.IsChecked != true ? Task.CompletedTask : SaveAsync(edit => edit["bash"] = (string)button.Tag!);

        Task SaveAsync(Action<JsonObject> change) => TrySaveAsync(change);

        async Task<bool> TrySaveAsync(Action<JsonObject> change)
        {
            if (busy)
            {
                return false;
            }

            busy = true;
            Controls.IsEnabled = false;
            Error.IsVisible = false;
            try
            {
                var edit = new JsonObject();
                foreach (var key in new[] { "bash", "network", "workspaceRoot", "allowWrite", "protectChangedFiles" })
                {
                    edit[key] = settings[key]?.DeepClone();
                }

                change(edit);
                settings = await api.SaveSandboxAsync(edit, token).ConfigureAwait(true);
                return true;
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    Error.Text = describe(error);
                    Error.IsVisible = true;
                }

                return false;
            }
            finally
            {
                RenderSandbox();
                Controls.IsEnabled = true;
                busy = false;
            }
        }

        async Task<bool> ChangeFolderAsync(string path, bool add)
        {
            if (busy)
            {
                return false;
            }

            busy = true;
            FolderControls.IsEnabled = false;
            FolderError.IsVisible = false;
            try
            {
                folders = await api.ChangeWorkspaceRootAsync(path, add, token).ConfigureAwait(true);
                RenderFolders();
                return true;
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    FolderError.Text = describe(error);
                    FolderError.IsVisible = true;
                }

                return false;
            }
            finally
            {
                FolderControls.IsEnabled = true;
                busy = false;
            }
        }

        void RenderSandbox()
        {
            rendering = true;
            WorkspaceRoot.Text = (string?)settings["workspaceRoot"] ?? string.Empty;
            Protect.IsChecked = (bool?)settings["protectChangedFiles"] == true;
            Network.IsChecked = (bool?)settings["network"] == true;
            AddRoot.IsEnabled = !string.IsNullOrWhiteSpace(ExtraRoot.Text);
            var written = (string?)settings["bash"] ?? string.Empty;
            var mode = (string?)settings["effectiveBash"] ?? written;
            var available = (bool?)settings["available"] == true;
            Confined.IsChecked = mode == "enforce";
            Unconfined.IsChecked = mode != "enforce";
            Confined.IsEnabled = available;
            NetworkRow.IsVisible = mode == "enforce";
            CommandHint.IsVisible = available;
            CommandHint.Text = Text((string?)settings["platform"] == "windows"
                ? "safety.on_windows_once_on_commands"
                : "safety.inside_the_sandbox_a_command");
            Unavailable.IsVisible = !available;
            WhyUnavailable.Text = (string?)settings["why"] ?? Text("safety.commands_run_unconfined_the_writable");
            ToolTip.SetTip(Confined, available ? null : WhyUnavailable.Text);
            Overruled.IsVisible = written.Length > 0 && written != mode;
            Overruled.Text = Text("common.actually_in_force") + " · " + Text("safety.configured_as_want_running_as", ("want", Mode(written)), ("now", Mode(mode)));
            ShadowNotice.IsVisible = (string?)settings["shadowedBy"] is { Length: > 0 };
            ShadowMessage.Text = Text("safety.path_declares_a_sandbox_too", ("path", (string?)settings["shadowedBy"] ?? string.Empty));
            WritableRows.Children.Clear();
            foreach (var path in Strings(settings["allowWrite"]))
            {
                var button = FolderButton(Text("common.delete"), "sandbox.remove-write-root", path);
                button.Click += async (_, _) => await SaveAsync(edit => edit["allowWrite"] = Strings(Strings(edit["allowWrite"]).Where(value => value != path))).ConfigureAwait(true);
                WritableRows.Children.Add(PathRow(path, button));
            }

            EffectivePaths.Children.Clear();
            foreach (var path in Strings(settings["effectiveWriteRoots"]).DefaultIfEmpty("—"))
            {
                EffectivePaths.Children.Add(PathLabel(path));
            }

            rendering = false;
        }

        void RenderFolders()
        {
            FolderControls.IsVisible = true;
            AddProject.IsEnabled = !string.IsNullOrWhiteSpace(ProjectPath.Text);
            PrimaryFolder.Text = (string?)folders["primary"] ?? "—";
            ProjectFolders.Children.Clear();
            foreach (var path in Strings(folders["extra"]))
            {
                var remove = FolderButton(Text("common.remove"), "folders.remove", path);
                remove.Click += async (_, _) => await ChangeFolderAsync(path, false).ConfigureAwait(true);
                ProjectFolders.Children.Add(PathRow(path, remove));
            }

            RemovalNote.IsVisible = ProjectFolders.Children.Count > 0;
            var pending = (int?)folders["pending"] ?? 0;
            Pending.IsVisible = pending > 0;
            Pending.Text = Text("session.n_panes_are_still_running", ("n", pending.ToString(CultureInfo.CurrentCulture)));
        }

        string Mode(string mode) => Text(mode == "enforce" ? "safety.jailed" : "safety.unconfined");
    }

    /// <summary>Commits an entry on Enter in the same way as its Add button.</summary>
    /// <param name="key">The key event.</param>
    /// <param name="commit">The requested edit.</param>
    /// <returns>The edit activity.</returns>
    private static async Task EnterAsync(KeyEventArgs key, Func<Task> commit)
    {
        if (key.Key == Key.Enter)
        {
            key.Handled = true;
            await commit().ConfigureAwait(true);
        }
    }

    /// <summary>Reads folder names without transforming their spelling.</summary>
    /// <param name="node">The folder list.</param>
    /// <returns>The saved paths.</returns>
    private static IEnumerable<string> Strings(JsonNode? node) => (node as JsonArray)?.Select(value => (string?)value).OfType<string>() ?? [];

    /// <summary>Packages folder paths for a kernel write.</summary>
    /// <param name="paths">The exact paths.</param>
    /// <returns>The encoded strings.</returns>
    private static JsonArray Strings(IEnumerable<string> paths) => new(paths.Select(path => (JsonNode?)JsonValue.Create(path)).ToArray());

    /// <summary>Creates one remove action.</summary>
    /// <param name="label">The visible name.</param>
    /// <param name="action">The automation identity.</param>
    /// <param name="path">The selected grant.</param>
    /// <returns>The button.</returns>
    private static Button FolderButton(string label, string action, string path)
    {
        var button = new Button { Content = label, Tag = path, Classes = { "preset-flat", "preset-muted", "safety-ghost" } };
        AutomationProperties.SetAutomationId(button, "action:" + action);
        return button;
    }

    /// <summary>Formats the displayed filesystem path.</summary>
    /// <param name="path">The exact path.</param>
    /// <returns>The path label.</returns>
    private static TextBlock PathLabel(string path) => new()
    {
        Text = path,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
        [!TextBlock.FontFamilyProperty] = StudioTypography.Resource("WorkbenchMonoFamily"),
    };

    /// <summary>Displays one saved path beside its matching action.</summary>
    /// <param name="path">The folder path.</param>
    /// <param name="button">The remove action.</param>
    /// <returns>The row.</returns>
    private static Grid PathRow(string path, Button button)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8,
            Children =
            {
                new Border
                {
                    Padding = new Avalonia.Thickness(8, 3),
                    CornerRadius = new Avalonia.CornerRadius(4),
                    [!Border.BackgroundProperty] = StudioTypography.Resource("OverlayBrush"),
                    Child = PathLabel(path),
                },
                button,
            },
        };
        Grid.SetColumn(button, 1);
        return row;
    }
}
