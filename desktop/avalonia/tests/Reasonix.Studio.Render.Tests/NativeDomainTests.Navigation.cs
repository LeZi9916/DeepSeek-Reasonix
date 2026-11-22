using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Events;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.Pages;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.Shell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>A real pointer click opens the row's workspace and preserves other drafts.</summary>
    /// <param name="refreshWhilePressed">Whether a canonical refresh arrives during the pointer press.</param>
    /// <returns>The complete mounted navigation.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARealSessionClickOpensItsWorkspaceAndRestoresItsDraft(bool refreshWhilePressed)
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(RasterBuilder));
        await session.Dispatch(
            () =>
            {
                using var reads = new ReferenceReads();
                using var http = new HttpClient(reads) { BaseAddress = new Uri("http://127.0.0.1:4000") };
                var opened = new List<string>();
                var viewed = new List<string>();
                var active = string.Empty;
                reads.Exchange = async request =>
                {
                    var path = request.RequestUri!.AbsolutePath;
                    if (path == "/tree/sessions/viewed")
                    {
                        viewed.Add((string?)JsonNode.Parse(await request.Content!.ReadAsStringAsync())?["path"] ?? string.Empty);
                        return new HttpResponseMessage(HttpStatusCode.NoContent);
                    }

                    if (path == "/runtimes" && request.Method == HttpMethod.Post)
                    {
                        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
                        active = (string?)body["sessionPath"] ?? string.Empty;
                        opened.Add(active);
                        return JsonReply(new JsonObject { ["id"] = active == "/b/chat.jsonl" ? "r2" : "r3", ["base"] = active == "/b/chat.jsonl" ? "/rt/r2" : "/rt/r3", ["root"] = "/b", ["sessionPath"] = active });
                    }

                    if (path == "/status")
                    {
                        var status = reads.Read<JsonObject>(path);
                        status["workspaceRoot"] = "/b";
                        status["sessionPath"] = active;
                        status["label"] = active;
                        return JsonReply(status);
                    }

                    if (path == "/history/page")
                    {
                        return new HttpResponseMessage(HttpStatusCode.NotFound);
                    }

                    if (path == "/history")
                    {
                        return JsonReply(new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "history for " + active, ["msgIndex"] = 0 }));
                    }

                    if (path is "/events" or "/tree/events")
                    {
                        return JsonReply(new JsonArray());
                    }

                    return null;
                };
                SessionEngine MakeEngine(RuntimeDescriptor? runtime = null) => new(new KernelClient(http), new SseEventTransport(http.BaseAddress.ToString(), http), descriptor => MakeEngine(descriptor), runtime);
                var engine = MakeEngine();
                var w = new MainWindow(false, engine, InterfaceLanguage.Zh, _ => Task.FromResult(false), new NavigationDrafts()) { Width = 1440, Height = 900 };
                w.Show();
                try
                {
                    var status = reads.Read<SessionStatus>("/status") with
                    {
                        WorkspaceRoot = "/a",
                        SessionPath = "/a/chat.jsonl",
                    };
                    w.AdoptComposer(status);
                    var draft = w.FindControl<TextBox>("Composer")!;
                    draft.Text = "keep the original draft";
                    var rail = w.FindControl<ProjectRail>("Projects")!;
                    rail.Show(new[] { new TreeWorkspace { Root = "/b", Name = "b", Sessions = new[] { new TreeSession { Path = "/b/chat.jsonl", Title = "target conversation", WorkspaceRoot = "/b" } } } }, status.SessionPath);
                    Dispatcher.UIThread.RunJobs();
                    var row = rail.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "action:session.open" && AutomationProperties.GetName(b) == "target conversation");
                    var point = row.TranslatePoint(new Point(80, 15), w)!.Value;
                    w.MouseMove(point);
                    w.MouseDown(point, MouseButton.Left);
                    if (refreshWhilePressed)
                    {
                        rail.Show(new[] { new TreeWorkspace { Root = "/b", Name = "b", Sessions = new[] { new TreeSession { Path = "/b/chat.jsonl", Title = "target conversation", WorkspaceRoot = "/b" } } } }, status.SessionPath);
                        Dispatcher.UIThread.RunJobs();
                    }

                    w.MouseUp(point, MouseButton.Left);
                    PumpNavigation(() => opened.Count == 1 && w.FindControl<TextBlock>("CrumbTitle")!.Text == "target conversation" && w.FindControl<SessionPage>("Conversation")!.State.Items.Count > 0);
                    Assert.Equal(new[] { "/b/chat.jsonl" }, opened);
                    Assert.Contains(w.FindControl<SessionPage>("Conversation")!.State.Items, item => item is Reasonix.Studio.State.Transcript.UserItem { Text: "history for /b/chat.jsonl" });
                    Assert.Equal(string.Empty, draft.Text);
                    draft.Text = "second draft";
                    rail.Show(new[] { new TreeWorkspace { Root = "/b", Name = "b", Sessions = new[] { new TreeSession { Path = "/b/chat.jsonl", Title = "target conversation", WorkspaceRoot = "/b" } } } }, "/b/chat.jsonl");
                    Dispatcher.UIThread.RunJobs();
                    row = rail.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "action:session.open" && AutomationProperties.GetName(b) == "target conversation");
                    row.Focus();
                    w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                    w.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal("second draft", draft.Text);
                    Assert.Single(opened);
                    Assert.Single(viewed, path => path == "/b/chat.jsonl");
                }
                finally
                {
                    w.Close();
                }
            },
            default);
    }

    /// <summary>Pumps native work until the navigation's visible result is ready.</summary>
    /// <param name="ready">The observable result predicate.</param>
    private static void PumpNavigation(Func<bool> ready)
    {
        Assert.True(
            SpinWait.SpinUntil(
                () =>
                {
                    Dispatcher.UIThread.RunJobs();
                    return ready();
                },
                TimeSpan.FromSeconds(4)),
            "session navigation never became visible");
    }

    /// <summary>Isolates navigation drafts from the reader's machine profile.</summary>
    private sealed class NavigationDrafts : Reasonix.Studio.Pages.Composer.IDraftStorage
    {
        /// <summary>Drafts scoped by canonical host and transcript.</summary>
        private readonly Dictionary<string, string> _values = [];

        /// <inheritdoc/>
        public string Read(string key) => _values.GetValueOrDefault(key, string.Empty);

        /// <inheritdoc/>
        public void Write(string key, string text) => _values[key] = text;
    }
}
