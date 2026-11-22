using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Kernel.Boundary;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.Pages;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.Shell;
using Reasonix.Studio.State.Transcript;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Reads and switches real Windows history without closing background runtimes.</summary>
    /// <returns>The actual isolated host and native pointer interaction.</returns>
    [Fact]
    public async Task RuntimeNavigationReadsRealWindowsSessionsAndKeepsDrafts()
    {
        var binary = HostLauncher.Locate();
        if (binary is null || !OperatingSystem.IsWindows())
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("STUDIO_TEST_HOST_REQUIRED"));
            return;
        }

        var scratch = Directory.CreateTempSubdirectory("studio-native-navigation-");
        Process? hosted = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var token = timeout.Token;
            var profile = scratch.CreateSubdirectory("profile");
            var first = scratch.CreateSubdirectory("project-a");
            var second = scratch.CreateSubdirectory("project-b");
            using var kernel = await HostLauncher.StartAsync(binary, Array.Empty<string>(), token, profile.FullName);
            hosted = Process.GetProcessById(kernel.Pid);
            using var http = new HttpClient(new CredentialHandler(kernel.Cookie())) { BaseAddress = kernel.Origin };
            await using var owner = new SessionEngine(kernel);
            await using var seedA = await owner.OpenProjectAsync(first.FullName, token);
            await using var seedB = await owner.OpenProjectAsync(second.FullName, token);
            var firstPath = await ImportAsync(first.FullName, "first real answer", "First fixture");
            var secondPath = await ImportAsync(second.FullName, "second real answer", "Second fixture");
            var occupiedPath = await ImportAsync(first.FullName, "occupied answer", "Occupied fixture");
            using var otherKernel = await HostLauncher.StartAsync(binary, Array.Empty<string>(), token, profile.FullName);
            await using var otherOwner = new SessionEngine(otherKernel);
            await using var occupied = await otherOwner.OpenProjectAsync(first.FullName, token, false, occupiedPath);
            var tree = await owner.TreeAsync(null, token);
            var baselineRuntimes = await owner.Pages.RuntimesAsync(token);
            var session = HeadlessUnitTestSession.StartNew(typeof(RasterBuilder));
            await session.Dispatch(
                async () =>
            {
                var window = new MainWindow(false, owner, InterfaceLanguage.Zh, _ => Task.FromResult(false), new Reasonix.Studio.Shell.Host.FileDrafts(Path.Combine(profile.FullName, "native-drafts"))) { Width = 1440, Height = 900 };
                window.Show();
                try
                {
                    window.AdoptComposer((await owner.StatusAsync(token).ConfigureAwait(true))!);
                    var rail = window.FindControl<ProjectRail>("Projects")!;
                    rail.Show(tree, null);
                    Dispatcher.UIThread.RunJobs();
                    var requested = 0;
                    rail.NavigateRequested += _ => requested++;
                    Click("First fixture");
                    Assert.Equal(1, requested);
                    await Wait(() => window.FindControl<SessionPage>("Conversation")!.State.Items.OfType<SayItem>().Any(item => item.Text == "first real answer")).ConfigureAwait(true);
                    Assert.Equal("First fixture", window.FindControl<TextBlock>("CrumbTitle")!.Text);
                    var draft = window.FindControl<TextBox>("Composer")!;
                    draft.Text = "draft in first project";
                    rail.Show(tree, firstPath);
                    Dispatcher.UIThread.RunJobs();
                    Click("Second fixture");
                    await Wait(() => window.FindControl<SessionPage>("Conversation")!.State.Items.OfType<SayItem>().Any(item => item.Text == "second real answer")).ConfigureAwait(true);
                    Assert.Equal("Second fixture", window.FindControl<TextBlock>("CrumbTitle")!.Text);
                    Assert.Equal(string.Empty, draft.Text);
                    draft.Text = "draft in second project";
                    var after = await owner.Pages.RuntimesAsync(token).ConfigureAwait(true);
                    Assert.Contains(after, runtime => runtime.SessionPath == firstPath);
                    Assert.Contains(after, runtime => runtime.SessionPath == secondPath);
                    Assert.True(after.Length >= baselineRuntimes.Length + 2);
                    rail.Show(tree, secondPath);
                    Dispatcher.UIThread.RunJobs();
                    Click("First fixture");
                    await Wait(() => draft.Text == "draft in first project").ConfigureAwait(true);
                    Assert.Contains(window.FindControl<SessionPage>("Conversation")!.State.Items.OfType<SayItem>(), item => item.Text == "first real answer");
                    using var refused = await http.PostAsJsonAsync("/tree/sessions/remove", new JsonObject { ["path"] = occupiedPath }, token).ConfigureAwait(true);
                    Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
                    rail.RequestAction(new WorkspaceAction("session.delete", new SessionOpenTarget(string.Empty, first.FullName, occupiedPath)));
                    Dispatcher.UIThread.RunJobs();
                    window.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:workspace.confirm").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Wait(() => window.FindControl<Border>("ErrorBar")!.IsVisible).ConfigureAwait(true);
                    Assert.False(string.IsNullOrWhiteSpace(window.FindControl<SelectableTextBlock>("ErrorText")!.Text));
                    Assert.Equal("draft in first project", draft.Text);
                    Assert.Contains(window.FindControl<SessionPage>("Conversation")!.State.Items.OfType<SayItem>(), item => item.Text == "first real answer");
                    Assert.True(File.Exists(occupiedPath));
                    window.FindControl<MenuItem>("BrowserAction")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    var browsers = window.FindControl<Reasonix.Studio.Controls.StudioSurfaceHost>("ManualBrowserHost")!;
                    var firstBrowser = browsers.Content!;
                    rail.Show(tree, firstPath);
                    Dispatcher.UIThread.RunJobs();
                    Click("Second fixture");
                    await Wait(() => draft.Text == "draft in second project").ConfigureAwait(true);
                    window.FindControl<MenuItem>("BrowserAction")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    var secondBrowser = browsers.Content!;
                    Assert.Equal(2, browsers.Children.Count);
                    Assert.True(firstBrowser.IsAttachedToVisualTree());
                    var firstTab = window.FindControl<Control>("PaneTabs")!.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:pane.activate" && AutomationProperties.GetName(button) == "First fixture");
                    var close = Assert.IsType<StackPanel>(firstTab.Parent).Children.OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:pane.close");
                    var closePoint = close.TranslatePoint(new Point(10, 10), window)!.Value;
                    window.MouseMove(closePoint);
                    window.MouseDown(closePoint, MouseButton.Left);
                    window.MouseUp(closePoint, MouseButton.Left);
                    await Wait(() => browsers.Children.Count == 1).ConfigureAwait(true);
                    Assert.Same(secondBrowser, browsers.Content);
                    Assert.False(firstBrowser.IsAttachedToVisualTree());
                    Assert.Equal("draft in second project", draft.Text);
                    Assert.DoesNotContain(await owner.Pages.RuntimesAsync(token).ConfigureAwait(true), runtime => runtime.SessionPath == firstPath);
                }
                finally
                {
                    window.Close();
                    Dispatcher.UIThread.RunJobs();
                }

                void Click(string title)
                {
                    var row = window.FindControl<ProjectRail>("Projects")!.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:session.open" && AutomationProperties.GetName(button) == title);
                    Assert.True(row.IsEffectivelyVisible, "Session row is hidden: " + row.Bounds);
                    row.BringIntoView();
                    Dispatcher.UIThread.RunJobs();
                    var point = row.TranslatePoint(new Point(80, 15), window)!.Value;
                    window.MouseMove(point);
                    window.MouseDown(point, MouseButton.Left);
                    window.MouseUp(point, MouseButton.Left);
                }

                return 0;

                async Task Wait(Func<bool> ready)
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    while (!ready() && watch.Elapsed < TimeSpan.FromSeconds(12))
                    {
                        await Task.Delay(10, token).ConfigureAwait(true);
                    }

                    var runtimeSummary = string.Empty;
                    using var probeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    try
                    {
                        using var probe = await http.GetAsync("/runtimes", probeTimeout.Token).ConfigureAwait(true);
                        runtimeSummary = await probe.Content.ReadAsStringAsync(probeTimeout.Token).ConfigureAwait(true);
                    }
                    catch (TaskCanceledException)
                    {
                        runtimeSummary = "independent authenticated /runtimes request timed out";
                    }

                    var navigation = (Reasonix.Studio.Shell.Session.WindowSessionCoordinator?)typeof(MainWindow).GetField("_navigation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window);
                    var pending = navigation is null ? null : typeof(Reasonix.Studio.Shell.Session.WindowSessionCoordinator).GetField("_opening", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(navigation) as System.Collections.IDictionary;
                    var tasks = pending is null ? string.Empty : string.Join(",", pending.Values.Cast<Task>().Select(task => task.Status));
                    Assert.True(ready(), "Real host navigation did not reach its acknowledged UI state. Title=" + window.FindControl<TextBlock>("CrumbTitle")!.Text + " Error=" + window.FindControl<SelectableTextBlock>("ErrorText")!.Text + " Runtimes=" + runtimeSummary + " Panes=" + navigation?.Panes.Count + " Tasks=" + tasks + " Sync=" + SynchronizationContext.Current?.GetType().FullName + " Progress=" + window.FindControl<Control>("NavigationProgress")!.IsVisible + " Items=" + string.Join(";", window.FindControl<SessionPage>("Conversation")!.State.Items.Select(item => item.ToString())));
                }
            },
                token);

            async Task<string> ImportAsync(string root, string answer, string title)
            {
                var archive = new JsonObject
                {
                    ["format"] = "reasonix-native-session",
                    ["version"] = 2,
                    ["records"] = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = "Isolated read-only history fixture." }, new JsonObject { ["role"] = "user", ["content"] = "fixture question" }, new JsonObject { ["role"] = "assistant", ["content"] = answer }),
                };
                using var imported = await http.PostAsJsonAsync("/tree/sessions/import-native?workspace=" + Uri.EscapeDataString(root), archive, token);
                Assert.True(imported.IsSuccessStatusCode, await imported.Content.ReadAsStringAsync(token));
                var saved = await imported.Content.ReadFromJsonAsync<JsonObject>(token);
                var path = (string)saved!["path"]!;
                await owner.Pages.WorkspaceActionAsync("sessions/rename", new JsonObject { ["path"] = path, ["title"] = title }, token);
                return path;
            }
        }
        finally
        {
            await RemoveHostProfileAsync(hosted, scratch);
        }
    }
}
