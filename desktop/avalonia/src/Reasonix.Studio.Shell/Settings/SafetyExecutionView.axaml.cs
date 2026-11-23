using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Shows the active command interpreter and coordination between project writers.</summary>
public sealed partial class SafetyExecutionView : SettingsFormView
{
    /// <summary>Initializes a new instance of the <see cref="SafetyExecutionView"/> class.</summary>
    public SafetyExecutionView() => InitializeComponent();

    /// <summary>Reads the two independently available execution settings.</summary>
    /// <param name="api">The current session's endpoints.</param>
    /// <param name="language">The interface language.</param>
    /// <param name="describe">Localizes a refusal.</param>
    /// <param name="token">The window lifetime.</param>
    /// <returns>The shell and write lease reads.</returns>
    internal async Task OpenAsync(StudioPageClient api, InterfaceLanguage language, Func<Exception, string> describe, CancellationToken token)
    {
        Translate(language);
        var shellGroup = Guid.NewGuid().ToString("N");
        var shell = new JsonObject();
        var lease = new JsonObject();
        var renderingLease = false;
        var shellBusy = false;
        var leaseBusy = false;
        Custom.Click += (_, _) =>
        {
            CustomForm.IsVisible = !CustomForm.IsVisible;
            if (CustomForm.IsVisible)
            {
                Executable.Focus();
            }
        };
        Executable.TextChanged += (_, _) => SavePath.IsEnabled = !string.IsNullOrWhiteSpace(Executable.Text);
        SavePath.Click += async (_, _) =>
        {
            var path = Executable.Text?.Trim() ?? string.Empty;
            var args = ArgumentsField.IsVisible
                ? (Arguments.Text ?? string.Empty).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                : [];
            var prefer = args.Length > 0 ? "bash" : KindOf(path);
            await SaveShellAsync(prefer, path, args).ConfigureAwait(true);
        };
        ClearPath.Click += async (_, _) => await SaveShellAsync((string?)shell["prefer"] ?? "auto", string.Empty, []).ConfigureAwait(true);
        Optimistic.IsCheckedChanged += async (_, _) =>
        {
            if (!renderingLease)
            {
                await SaveLeaseAsync("optimistic", JsonValue.Create(Optimistic.IsChecked == true)).ConfigureAwait(true);
            }
        };
        MaxWait.Open(
            Text("safety.minutes"),
            Text("safety.longest_wait_for_another_session"),
            "write-lease.max-wait",
            async text =>
            {
                if (leaseBusy)
                {
                    return;
                }

                if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
                {
                    LeaseError.Text = Text("common.enter_a_whole_number_from", ("min", "0"), ("max", int.MaxValue.ToString(CultureInfo.CurrentCulture)));
                    LeaseError.IsVisible = true;
                }
                else if (minutes != (int?)lease["maxWaitMinutes"])
                {
                    await SaveLeaseAsync("maxWaitMinutes", JsonValue.Create(minutes)).ConfigureAwait(true);
                }
            });
        await Task.WhenAll(ReadShellAsync(), ReadLeaseAsync()).ConfigureAwait(true);

        async Task ReadShellAsync()
        {
            try
            {
                shell = await api.ShellAsync(token).ConfigureAwait(true);
                Shell.IsVisible = true;
                RenderShell();
            }
            catch (Exception)
            {
                ShellError.Text = Text("app.could_not_read_the_shell");
                ShellError.IsVisible = !token.IsCancellationRequested;
            }
        }

        async Task ReadLeaseAsync()
        {
            try
            {
                lease = await api.WriteLeaseAsync(token).ConfigureAwait(true);
                Lease.IsVisible = true;
                RenderLease();
            }
            catch (Exception)
            {
                LeaseError.Text = Text("safety.could_not_read_the_multi");
                LeaseError.IsVisible = !token.IsCancellationRequested;
            }
        }

        async Task SaveShellAsync(string prefer, string path, string[] args)
        {
            if (shellBusy)
            {
                return;
            }

            shellBusy = true;
            Shell.IsEnabled = false;
            ShellError.IsVisible = false;
            try
            {
                var fields = new JsonObject { ["prefer"] = prefer, ["path"] = path };
                if (args.Length > 0)
                {
                    fields["args"] = new JsonArray(args.Select(arg => (JsonNode?)JsonValue.Create(arg)).ToArray());
                }

                shell = await api.SaveShellAsync(fields, token).ConfigureAwait(true);
                RenderShell();
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    ShellError.Text = describe(error);
                    ShellError.IsVisible = true;
                    RenderExecutors();
                }
            }
            finally
            {
                Shell.IsEnabled = true;
                shellBusy = false;
            }
        }

        async Task SaveLeaseAsync(string key, JsonValue? value)
        {
            if (leaseBusy)
            {
                return;
            }

            leaseBusy = true;
            Lease.IsEnabled = false;
            LeaseError.IsVisible = false;
            try
            {
                lease = await api.SaveWriteLeaseAsync(new JsonObject { [key] = value }, token).ConfigureAwait(true);
            }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                {
                    LeaseError.Text = describe(error);
                    LeaseError.IsVisible = true;
                }
            }
            finally
            {
                RenderLease();
                Lease.IsEnabled = true;
                leaseBusy = false;
            }
        }

        void RenderShell()
        {
            Executable.Text = (string?)shell["path"] ?? string.Empty;
            Executable.PlaceholderText = (string?)shell["auto"]?["path"];
            Arguments.Text = string.Join(Environment.NewLine, (shell["args"] as JsonArray ?? []).Select(arg => (string?)arg));
            Arguments.PlaceholderText = "-d\nUbuntu\n--\n/bin/bash";
            ArgumentsField.IsVisible = (string?)shell["platform"] == "windows";
            LauncherRow.IsVisible = shell["argv"] is JsonArray { Count: > 0 };
            CurrentLauncher.Text = string.Join(" ", (shell["argv"] as JsonArray ?? []).Select(arg => (string?)arg)) + " -c …";
            LauncherWarning.IsVisible = shell["args"] is JsonArray { Count: > 0 } && !LauncherRow.IsVisible;
            ClearPath.IsVisible = Executable.Text.Length > 0;
            SavePath.IsEnabled = Executable.Text.Trim().Length > 0;
            var version = (string?)shell["effective"]?["version"];
            Effective.Text = ShellName((string?)shell["effective"]?["name"]) + (string.IsNullOrEmpty(version) ? string.Empty : " " + version)
                + " · " + ((string?)shell["effective"]?["path"] ?? "—");
            RenderExecutors();
        }

        void RenderExecutors()
        {
            Executors.Children.Clear();
            var options = (shell["options"] as JsonArray)?.OfType<JsonObject>().ToArray() ?? [];
            var automatic = (string?)shell["prefer"] == "auto" && string.IsNullOrEmpty((string?)shell["path"]);
            var effective = shell["effective"];
            AddExecutor(Text("common.auto"), "auto", string.Empty, automatic);
            foreach (var item in options)
            {
                var name = ShellName((string?)item["name"]);
                if ((string?)item["version"] is { Length: > 0 } version)
                {
                    name += "  " + version;
                }

                var prefer = (string?)item["prefer"] ?? "auto";
                var path = (string?)item["path"] ?? string.Empty;
                var pinned = options.Count(option => (string?)option["prefer"] == prefer) > 1;
                AddExecutor(name, prefer, pinned ? path : string.Empty, !automatic && path == (string?)effective?["path"]);
            }

            var note = (string?)effective?["name"] switch
            {
                "pwsh" => Text("app.understands_and_but_the_syntax"),
                "powershell" => Text("app.does_not_accept_or_chained"),
                _ => string.Empty,
            };
            ShellNote.Text = automatic
                ? Text("app.finds_one_itself_preferring_a") + " " + ShellName((string?)shell["auto"]?["name"])
                : ((string?)effective?["path"] ?? string.Empty) + (note.Length == 0 ? string.Empty : " · " + note);
            NoBash.IsVisible = (string?)shell["platform"] == "windows" && !options.Any(item => (string?)item["prefer"] == "bash");
        }

        void AddExecutor(string label, string prefer, string path, bool current)
        {
            var button = new RadioButton
            {
                Content = label,
                Tag = prefer,
                GroupName = shellGroup,
                Classes = { "segmented" },
                IsChecked = current,
            };
            AutomationProperties.SetAutomationId(button, "action:shell.executor");
            Executors.Children.Add(button);
            button.IsCheckedChanged += async (_, _) =>
            {
                if (button.IsChecked == true)
                {
                    await SaveShellAsync(prefer, path, []).ConfigureAwait(true);
                }
            };
        }

        void RenderLease()
        {
            renderingLease = true;
            Optimistic.IsChecked = (bool?)lease["optimistic"] == true;
            MaxWait.SetValue((int?)lease["maxWaitMinutes"] ?? 0);
            renderingLease = false;
        }
    }

    /// <summary>Displays the installed product rather than its binary name.</summary>
    /// <param name="name">The kernel shell name.</param>
    /// <returns>The displayed interpreter.</returns>
    private static string ShellName(string? name) => name switch
    {
        "git-bash" => "Git Bash",
        "pwsh" => "PowerShell 7",
        "powershell" => "Windows PowerShell",
        _ => name ?? "—",
    };

    /// <summary>Infers the command syntax family from the path the user entered.</summary>
    /// <param name="path">The entered executable path.</param>
    /// <returns>The family sent to the kernel.</returns>
    private static string KindOf(string path)
    {
        if (path.Contains("pwsh", StringComparison.OrdinalIgnoreCase))
        {
            return "pwsh";
        }

        return path.Contains("powershell", StringComparison.OrdinalIgnoreCase) ? "powershell" : "bash";
    }
}
