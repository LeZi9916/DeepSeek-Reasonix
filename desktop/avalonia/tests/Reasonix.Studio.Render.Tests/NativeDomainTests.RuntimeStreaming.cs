using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Kernel.Boundary;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.Pages;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.Shell;
using Reasonix.Studio.State.Transcript;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>A real kernel keeps generating in a background pane while native focus and drafts switch.</summary>
    /// <returns>The isolated loopback provider, real host and native pointer interaction.</returns>
    [Fact]
    public async Task RuntimeSidebarKeepsRealStreamingTurnAliveAcrossFocus()
    {
        var binary = HostLauncher.Locate();
        if (binary is null || !OperatingSystem.IsWindows())
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("STUDIO_TEST_HOST_REQUIRED"));
            return;
        }

        var scratch = Directory.CreateTempSubdirectory("studio-sidebar-stream-");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var token = timeout.Token;
            await using var provider = new SidebarDelayedProvider();
            var profile = scratch.CreateSubdirectory("profile");
            var firstRoot = scratch.CreateSubdirectory("first");
            var secondRoot = scratch.CreateSubdirectory("second");
            await File.WriteAllTextAsync(
                Path.Combine(profile.FullName, "config.toml"),
                $$"""
                default_model = "local/slow"
                credentials_store = "file"
                [agent]
                system_prompt = "Return the requested deterministic fixture text."
                title_model = "local/title"
                [codegraph]
                enabled = false
                [[providers]]
                name = "local"
                kind = "openai"
                base_url = "{{provider.Origin}}"
                models = ["slow", "title"]
                default = "slow"
                api_key = "local-test-only"
                no_proxy = true
                context_window = 32768
                """,
                token);
            using var kernel = await HostLauncher.StartAsync(binary, Array.Empty<string>(), token, profile.FullName);
            await using var owner = new SessionEngine(kernel);
            await using var first = await owner.OpenProjectAsync(firstRoot.FullName, token, preset: "builtin:minimal");
            await using var second = await owner.OpenProjectAsync(secondRoot.FullName, token, preset: "builtin:minimal");
            using var http = new HttpClient(new CredentialHandler(kernel.Cookie())) { BaseAddress = kernel.Origin };
            var archive = new JsonObject { ["format"] = "reasonix-native-session", ["version"] = 2, ["records"] = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = "Local streaming navigation fixture." }, new JsonObject { ["role"] = "user", ["content"] = "second question" }, new JsonObject { ["role"] = "assistant", ["content"] = "second ready" }) };
            using var imported = await http.PostAsJsonAsync("/tree/sessions/import-native?workspace=" + Uri.EscapeDataString(secondRoot.FullName), archive, token);
            Assert.True(imported.IsSuccessStatusCode, await imported.Content.ReadAsStringAsync(token));
            var secondPath = (string)(await imported.Content.ReadFromJsonAsync<JsonObject>(token))!["path"]!;
            await owner.Pages.WorkspaceActionAsync("sessions/rename", new JsonObject { ["path"] = secondPath, ["title"] = "Other fixture" }, token);
            var session = HeadlessUnitTestSession.StartNew(typeof(RasterBuilder));
            await session.Dispatch(
                async () =>
                {
                    var window = new MainWindow(false, first, InterfaceLanguage.En, _ => Task.FromResult(false), new Reasonix.Studio.Shell.Host.FileDrafts(Path.Combine(profile.FullName, "drafts"))) { Width = 1440, Height = 900 };
                    var requested = 0;
                    window.FindControl<ProjectRail>("Projects")!.NavigateRequested += _ => requested++;
                    window.Show();
                    try
                    {
                        var status = (await first.StatusAsync(token).ConfigureAwait(true))!;
                        window.AdoptComposer(status);
                        await window.OpenTargetAsync(new SessionOpenTarget(string.Empty, status.WorkspaceRoot, status.SessionPath)).ConfigureAwait(true);
                        var draft = window.FindControl<TextBox>("Composer")!;
                        draft.Text = "background-stream-fixture";
                        Dispatcher.UIThread.RunJobs();
                        ClickControl(window.FindControl<Button>("Send")!);
                        await Wait(() => provider.Started.Task.IsCompleted && first.State.Running && Text(first).Contains("first segment", StringComparison.Ordinal)).ConfigureAwait(true);
                        status = (await first.StatusAsync(token).ConfigureAwait(true))!;
                        var firstPath = status.SessionPath;
                        Assert.False(string.IsNullOrEmpty(firstPath));
                        window.AdoptComposer(status);
                        draft.Text = "first unsent draft";
                        var tree = await owner.TreeAsync(null, token).ConfigureAwait(true);
                        window.FindControl<ProjectRail>("Projects")!.Show(tree, firstPath);
                        Dispatcher.UIThread.RunJobs();
                        ClickSession(secondPath);
                        await Wait(() => window.FindControl<SessionPage>("Conversation")!.State.Items.OfType<SayItem>().Any(item => item.Text == "second ready")).ConfigureAwait(true);
                        Assert.Equal(string.Empty, draft.Text);
                        draft.Text = "second unsent draft";
                        provider.Advance.TrySetResult(true);
                        await Wait(() => Text(first).Contains("background progress", StringComparison.Ordinal)).ConfigureAwait(true);
                        Assert.True(first.State.Running);
                        Assert.True((await first.StatusAsync(token).ConfigureAwait(true))!.Running);
                        Assert.Contains(await owner.Pages.RuntimesAsync(token).ConfigureAwait(true), runtime => runtime.Id == first.Runtime!.Id);
                        Assert.DoesNotContain(window.FindControl<SessionPage>("Conversation")!.State.Items.OfType<SayItem>(), item => item.Text.Contains("background progress", StringComparison.Ordinal));
                        window.FindControl<ProjectRail>("Projects")!.Show(await owner.TreeAsync(null, token).ConfigureAwait(true), secondPath);
                        Dispatcher.UIThread.RunJobs();
                        ClickSession(firstPath);
                        await Wait(() => draft.Text == "first unsent draft" && window.FindControl<SessionPage>("Conversation")!.State.Running).ConfigureAwait(true);
                        Assert.Contains(window.FindControl<SessionPage>("Conversation")!.State.Items.OfType<SayItem>(), item => item.Text.Contains("background progress", StringComparison.Ordinal));
                        provider.Finish.TrySetResult(true);
                        await Wait(() => !first.State.Running && Text(first).Contains("finished", StringComparison.Ordinal)).ConfigureAwait(true);
                        ClickSession(secondPath);
                        await Wait(() => draft.Text == "second unsent draft").ConfigureAwait(true);
                        Assert.Equal(1, provider.SlowRequests);
                        Assert.Equal(string.Empty, provider.Failure);
                    }
                    finally
                    {
                        window.Close();
                        Dispatcher.UIThread.RunJobs();
                    }

                    return 0;

                    void ClickSession(string path)
                    {
                        var rail = window.FindControl<ProjectRail>("Projects")!;
                        var entry = rail.SearchEntries.Single(entry => entry.Target.Host.Length == 0 && entry.Target.SessionPath == path);
                        var title = entry.Session.Title.Length > 0 ? entry.Session.Title : entry.Session.Name;
                        var row = rail.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:session.open" && AutomationProperties.GetName(button) == title);
                        row.BringIntoView();
                        Dispatcher.UIThread.RunJobs();
                        row = rail.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:session.open" && AutomationProperties.GetName(button) == title);
                        ClickControl(row);
                    }

                    void ClickControl(Control control)
                    {
                        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
                        window.MouseMove(point);
                        window.MouseDown(point, MouseButton.Left);
                        window.MouseUp(point, MouseButton.Left);
                        Dispatcher.UIThread.RunJobs();
                    }

                    async Task Wait(Func<bool> ready)
                    {
                        var until = DateTime.UtcNow.AddSeconds(15);
                        while (!ready() && DateTime.UtcNow < until)
                        {
                            await Task.Delay(10, token).ConfigureAwait(true);
                        }

                        if (!ready())
                        {
                            window.CaptureRenderedFrame()?.Save("D:/TempFiles/sidebar-stream-failure.png", PngBitmapEncoderOptions.Default);
                        }

                        Assert.True(ready(), "Real streaming state did not arrive. Provider=" + provider.Failure + " Requests=" + provider.SlowRequests + " Clicked=" + requested + " Title=" + window.FindControl<TextBlock>("CrumbTitle")!.Text + " Progress=" + window.FindControl<Control>("NavigationProgress")!.IsVisible + " Current=" + string.Join(";", window.FindControl<SessionPage>("Conversation")!.State.Items.OfType<SayItem>().Select(item => item.Text)) + " First=" + Text(first) + " Error=" + window.FindControl<SelectableTextBlock>("ErrorText")!.Text + " Host=" + kernel.DiagnosticTail);
                    }
                },
                token);
        }
        finally
        {
            scratch.Delete(true);
        }

        static string Text(SessionEngine engine) => string.Join("\n", engine.State.Items.OfType<SayItem>().Select(item => item.Text));
    }
}
