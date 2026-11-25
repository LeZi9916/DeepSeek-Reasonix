using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Events;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Events;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.Pages;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.Shell;
using Reasonix.Studio.Shell.Workspace;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>A late previous open cannot replace the newer visible session or its draft.</summary>
    /// <returns>The mounted asynchronous sidebar navigation.</returns>
    [Fact]
    public Task SidebarLateOpenCannotTakeFocusFromLaterClick() => SidebarNavigationOrderAsync(false);

    /// <summary>A refused open releases its pending identity so another click can retry.</summary>
    /// <returns>The mounted refusal and acknowledged retry.</returns>
    [Fact]
    public Task SidebarRefusedOpenCanRetryOnNextClick() => SidebarNavigationOrderAsync(true);

    /// <summary>An existing tab selection invalidates an earlier slow sidebar open.</summary>
    /// <returns>The actual row-then-tab pointer ordering.</returns>
    [Fact]
    public Task SidebarLateOpenCannotTakeFocusFromExistingTab() => SidebarNavigationOrderAsync(false, true);

    /// <summary>A recent selection clears an archived scope, unfolds its project and focuses the actual selected row.</summary>
    /// <returns>The complete recent-to-workspace interaction.</returns>
    [Fact]
    public Task SidebarRecentSelectionClearsScopeUnfoldsAndLocatesTarget() => SidebarNavigationOrderAsync(false, false, true);

    /// <summary>A second request for an opening target waits for the shared runtime acknowledgement.</summary>
    /// <returns>The repeated navigation completion check.</returns>
    [Fact]
    public Task SidebarRepeatedPendingOpenWaitsForAcknowledgement() => SidebarNavigationOrderAsync(false, coalesced: true);

    /// <summary>The transcript is usable while the sidebar refresh still waits on the host.</summary>
    /// <returns>The mounted conversation and its delayed sidebar.</returns>
    [Fact]
    public Task SidebarRefreshDoesNotHoldConversationOpen() => SidebarNavigationOrderAsync(false, coalesced: true, deferTree: true);

    /// <summary>Drives real pointer events through the window with controllable HTTP completion.</summary>
    /// <param name="retry">Whether the first selected target is refused.</param><param name="retained">Whether the final target is an existing tab.</param>
    /// <param name="recent">Whether recent history locates a target hidden by the current sidebar scope.</param>
    /// <param name="coalesced">Whether two callers await the same opening target.</param>
    /// <param name="deferTree">Whether the focused runtime has a pending sidebar read.</param>
    /// <returns>The navigation boundary check.</returns>
    private static async Task SidebarNavigationOrderAsync(bool retry, bool retained = false, bool recent = false, bool coalesced = false, bool deferTree = false)
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(RasterBuilder));
        await session.Dispatch(
            async () =>
            {
                using var reads = new ReferenceReads();
                using var http = new HttpClient(reads) { BaseAddress = new Uri("http://127.0.0.1:4000") };
                var clients = new List<HttpClient>();
                var exchanges = new ConcurrentQueue<string>();
                var treeRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var treeResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var slow = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
                var requests = 0;
                var tree = new[]
                {
                    new TreeWorkspace { Root = "/slow-project", Name = "slow project", Sessions = new[] { new TreeSession { Path = "/slow.jsonl", Title = "Slow session" } } },
                    new TreeWorkspace { Root = "/fast-project", Name = "fast project", Sessions = new[] { new TreeSession { Path = "/fast.jsonl", Title = "Fast session" } } },
                };
                HttpResponseMessage Descriptor(string path) => JsonReply(new JsonObject { ["id"] = path == "/slow.jsonl" ? "r8" : "r9", ["base"] = path == "/slow.jsonl" ? "/rt/r8" : "/rt/r9", ["root"] = path == "/slow.jsonl" ? "/slow-project" : "/fast-project", ["sessionPath"] = path });
                reads.Exchange = async request =>
                {
                    exchanges.Enqueue(request.Method + " " + request.RequestUri!.PathAndQuery);
                    if (request.RequestUri!.AbsolutePath == "/tree")
                    {
                        if (request.RequestUri.Query.Contains("order=recent", StringComparison.Ordinal))
                        {
                            return JsonReply(JsonSerializer.SerializeToNode(tree.Select(workspace => workspace with { RecentOffset = 0 }).ToArray())!);
                        }

                        if (request.RequestUri.Query.Contains("scope=archived", StringComparison.Ordinal))
                        {
                            return JsonReply(JsonSerializer.SerializeToNode(new[] { new TreeWorkspace { Root = "/fast-project", Name = "fast project", Sessions = new[] { new TreeSession { Path = "/archived.jsonl", Title = "archived", Archived = true } } } })!);
                        }

                        return JsonReply(JsonSerializer.SerializeToNode(tree)!);
                    }

                    if (request.RequestUri.AbsolutePath == "/runtimes" && request.Method == HttpMethod.Post)
                    {
                        var path = (string)JsonNode.Parse(await request.Content!.ReadAsStringAsync())!["sessionPath"]!;
                        if (path == "/slow.jsonl")
                        {
                            requests++;
                            if (requests == 1)
                            {
                                return retry ? new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("{}") } : await slow.Task;
                            }
                        }

                        return Descriptor(path);
                    }

                    return null;
                };
                SessionEngine MakeEngine(RuntimeDescriptor? runtime = null)
                {
                    if (runtime is null)
                    {
                        return new SessionEngine(new KernelClient(http), new SidebarIdleTransport(), descriptor => MakeEngine(descriptor));
                    }

                    var paneReads = new ReferenceReads();
                    paneReads.Exchange = async request =>
                    {
                        exchanges.Enqueue(request.Method + " " + request.RequestUri!.PathAndQuery);
                        var path = request.RequestUri!.AbsolutePath;
                        HttpResponseMessage? response = null;
                        if (path == "/status")
                        {
                            var status = paneReads.Read<JsonObject>(path);
                            status["workspaceRoot"] = runtime.Root;
                            status["sessionPath"] = runtime.SessionPath;
                            status["label"] = runtime.SessionPath;
                            response = JsonReply(status);
                        }
                        else if (path == "/history/page")
                        {
                            response = new HttpResponseMessage(HttpStatusCode.NotFound);
                        }
                        else if (path == "/history")
                        {
                            response = JsonReply(new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "body " + runtime.SessionPath, ["msgIndex"] = 0 }));
                        }
                        else if (path == "/tree")
                        {
                            if (deferTree)
                            {
                                treeRequested.TrySetResult();
                                await treeResponse.Task;
                            }

                            response = JsonReply(JsonSerializer.SerializeToNode(tree)!);
                        }

                        return response;
                    };
                    var paneHttp = new HttpClient(paneReads) { BaseAddress = http.BaseAddress };
                    clients.Add(paneHttp);
                    return new SessionEngine(new KernelClient(paneHttp), new SidebarIdleTransport(), descriptor => MakeEngine(descriptor), runtime);
                }

                var window = new MainWindow(
                    false,
                    MakeEngine(),
                    InterfaceLanguage.En,
                    _ => Task.FromResult(false),
                    new NavigationDrafts(),
                    new Reasonix.Studio.Shell.Host.WorkspacePreferences { ShowSessionTabs = true })
                {
                    Width = 1440,
                    Height = 900,
                };
                window.Show();
                try
                {
                    window.AdoptComposer(reads.Read<SessionStatus>("/status") with
                    {
                        WorkspaceRoot = "/start",
                        SessionPath = "/start.jsonl",
                    });
                    window.FindControl<ProjectRail>("Projects")!.Show(tree, null);
                    Dispatcher.UIThread.RunJobs();
                    if (coalesced)
                    {
                        var target = new SessionOpenTarget(string.Empty, "/slow-project", "/slow.jsonl");
                        var opening = window.OpenTargetAsync(target);
                        await Wait(() => requests == 1);
                        var repeated = window.OpenTargetAsync(target);
                        Assert.False(repeated.IsCompleted, "The second caller returned before the shared runtime was acknowledged.");
                        slow.SetResult(Descriptor("/slow.jsonl"));
                        await Task.WhenAll(opening, repeated).WaitAsync(TimeSpan.FromSeconds(4));
                        if (deferTree)
                        {
                            await treeRequested.Task.WaitAsync(TimeSpan.FromSeconds(4));
                            Assert.False(window.FindControl<Control>("NavigationProgress")!.IsVisible);
                            Assert.Contains(window.FindControl<SessionPage>("Conversation")!.State.Items.OfType<UserItem>(), item => item.Text == "body /slow.jsonl");
                            Assert.False(treeResponse.Task.IsCompleted);
                        }

                        Assert.Equal(1, requests);
                        Assert.Equal("Slow session", window.FindControl<TextBlock>("CrumbTitle")!.Text);
                        return 0;
                    }

                    if (recent)
                    {
                        var rail = window.FindControl<ProjectRail>("Projects")!;
                        var scope = Assert.IsType<MenuFlyout>(rail.FindControl<Button>("Scope")!.Flyout);
                        scope.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "action:session.scope.archived").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
                        await Wait(() => rail.GetVisualDescendants().OfType<Button>().Any(button => AutomationProperties.GetName(button) == "archived"));
                        rail.FindControl<Button>("FoldAll")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                        rail.FindControl<Button>("RecentTab")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                        Dispatcher.UIThread.RunJobs();
                        var panel = Assert.Single(window.GetVisualDescendants().OfType<RecentSessionsPanel>());
                        await Wait(() => panel.FindControl<ListBox>("Rows")!.ItemCount == 2);
                        Dispatcher.UIThread.RunJobs();
                        Assert.True(panel.FindControl<TextBox>("Query")!.Focus());
                        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, string.Empty);
                        window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, string.Empty);
                        Assert.True(panel.FindControl<ListBox>("Rows")!.IsKeyboardFocusWithin);
                        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                        await Wait(() => !panel.IsVisible && !rail.FindControl<Button>("ClearScope")!.IsVisible && rail.GetVisualDescendants().OfType<Button>().Any(button => button.Classes.Contains("session-open") && button.IsKeyboardFocusWithin));
                        var selected = rail.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:session.open" && AutomationProperties.GetName(button) == "Fast session");
                        Assert.True(selected.IsEffectivelyVisible);
                        Assert.True(selected.IsKeyboardFocusWithin);
                        Assert.Equal("Fast session", window.FindControl<TextBlock>("CrumbTitle")!.Text);
                        return 0;
                    }

                    if (retained)
                    {
                        Click("Fast session");
                        await Wait(() => window.FindControl<TextBlock>("CrumbTitle")!.Text == "Fast session");
                        await Wait(() => window.FindControl<ProjectRail>("Projects")!.GetVisualDescendants().OfType<Button>()
                            .Any(button => AutomationProperties.GetAutomationId(button) == "action:session.open" && AutomationProperties.GetName(button) == "Slow session"));
                    }

                    Click("Slow session");
                    await Wait(() => requests == 1);
                    if (retry)
                    {
                        await Wait(() => window.FindControl<Border>("ErrorBar")!.IsVisible);
                        await Task.Delay((window.GetPlatformSettings()?.GetDoubleTapTime(PointerType.Mouse) ?? TimeSpan.FromMilliseconds(500)) + TimeSpan.FromMilliseconds(40));
                        Click("Slow session");
                        await Wait(() => window.FindControl<TextBlock>("CrumbTitle")!.Text == "Slow session");
                        Assert.Equal(2, requests);
                    }
                    else
                    {
                        if (retained)
                        {
                            var tab = window.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:pane.activate" && AutomationProperties.GetName(button) == "Fast session");
                            var point = tab.TranslatePoint(new Point(tab.Bounds.Width / 2, tab.Bounds.Height / 2), window)!.Value;
                            window.MouseMove(point);
                            window.MouseDown(point, MouseButton.Left);
                            window.MouseUp(point, MouseButton.Left);
                            Dispatcher.UIThread.RunJobs();
                        }
                        else
                        {
                            Click("Fast session");
                        }

                        await Wait(() => window.FindControl<TextBlock>("CrumbTitle")!.Text == "Fast session");
                        window.FindControl<TextBox>("Composer")!.Text = "newer draft";
                        slow.SetResult(Descriptor("/slow.jsonl"));
                        await Wait(() => !window.FindControl<Control>("NavigationProgress")!.IsVisible && clients.Count == 2);
                        await Task.Delay(60);
                        Assert.Equal("Fast session", window.FindControl<TextBlock>("CrumbTitle")!.Text);
                        Assert.Equal("newer draft", window.FindControl<TextBox>("Composer")!.Text);
                        Assert.Contains(window.FindControl<SessionPage>("Conversation")!.State.Items.OfType<UserItem>(), item => item.Text == "body /fast.jsonl");
                    }
                }
                finally
                {
                    window.Close();
                    treeResponse.TrySetResult();
                    foreach (var client in clients)
                    {
                        client.Dispose();
                    }
                }

                void Click(string name)
                {
                    var row = window.FindControl<ProjectRail>("Projects")!.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:session.open" && AutomationProperties.GetName(button) == name);
                    var point = row.TranslatePoint(new Point(80, 15), window)!.Value;
                    window.MouseMove(point);
                    window.MouseDown(point, MouseButton.Left);
                    window.MouseUp(point, MouseButton.Left);
                    Dispatcher.UIThread.RunJobs();
                }

                async Task Wait(Func<bool> ready)
                {
                    var stop = DateTime.UtcNow.AddSeconds(4);
                    while (!ready() && DateTime.UtcNow < stop)
                    {
                        await Task.Delay(5);
                    }

                    Assert.True(ready(), "The acknowledged sidebar state did not arrive: " + window.FindControl<TextBlock>("CrumbTitle")!.Text + " " + window.FindControl<SelectableTextBlock>("ErrorText")!.Text + " " + string.Join(", ", exchanges.TakeLast(12)));
                }

                return 0;
            },
            default);
    }

    /// <summary>Retains an event subscription without introducing reconnect work into navigation tests.</summary>
    private sealed class SidebarIdleTransport : IEventTransport
    {
        /// <inheritdoc/>
        public Task<ReplayResult> ReplayAsync(long after, CancellationToken cancellationToken) => Task.FromResult(new ReplayResult(Array.Empty<WireFrame>(), true, after));

        /// <inheritdoc/>
        public async IAsyncEnumerable<WireFrame> StreamAsync(Func<long?> cursor, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            yield break;
        }
    }
}
