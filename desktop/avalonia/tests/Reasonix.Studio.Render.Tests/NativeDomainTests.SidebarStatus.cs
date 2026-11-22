using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.State;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Reads remote projects and routes row mutations through their machine.</summary>
    /// <returns>The mounted sidebar interaction.</returns>
    [Fact]
    public Task SidebarLoadsRemoteProjectsAndWritesTheirHost() => WithSettings("conversation", (w, reads) =>
    {
        var pinned = false;
        var writes = 0;
        reads.Exchange = async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/remotes/gpu-box/tree")
            {
                return JsonReply(JsonNode.Parse("""[{"root":"/srv/training","name":"training","sessions":[{"path":"/remote/session","title":"Remote answer","unread":true}]}]""")!);
            }

            if (path == "/remotes/gpu-box/sessions/pin")
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
                Assert.Equal("/remote/session", (string?)body["path"]);
                pinned = (bool)body["pinned"]!;
                writes++;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return null;
        };
        w.FindControl<TextBox>("RailSearch")!.Text = " ";
        Dispatcher.UIThread.RunJobs();
        var rail = w.FindControl<ProjectRail>("Projects")!;
        Assert.Contains(rail.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "gpu-box" && text.IsEffectivelyVisible);
        Assert.Contains(rail.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "安装 reasonix" && text.IsEffectivelyVisible);
        var open = rail.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Remote answer" && AutomationProperties.GetAutomationId(button) == "action:session.open");
        Assert.IsType<MenuFlyout>(open.ContextFlyout).Items.OfType<MenuItem>().ElementAt(1).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(pinned);
        Assert.Equal(1, writes);
        var remoteFold = rail.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "training");
        remoteFold.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        w.FindControl<TextBox>("RailSearch")!.Text = "  ";
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(rail.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetName(button) == "Remote answer" && button.IsEffectivelyVisible);
    });

    /// <summary>Open idle panes retain their timestamp until a real turn begins.</summary>
    /// <returns>The sidebar state transitions.</returns>
    [Fact]
    public Task SidebarUsesSessionTimesAndActualRunningState() => WithSettings("conversation", (w, _) =>
    {
        var rail = w.FindControl<ProjectRail>("Projects")!;
        rail.Show(
            new[]
            {
                new TreeWorkspace
                {
                    Root = "/project",
                    Name = "project",
                    Sessions = new[]
                    {
                        new TreeSession { Path = "/session", Title = "idle pane", RuntimeId = "r1", LastActiveAt = DateTimeOffset.Now.AddMinutes(-10).ToUnixTimeMilliseconds() },
                    },
                },
            },
            "/session");
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(rail.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "10 分钟" && text.IsEffectivelyVisible);
        Assert.DoesNotContain(rail.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>(), mark => mark.Name == "Running" && mark.IsEffectivelyVisible);
        typeof(Reasonix.Studio.Shell.MainWindow).GetMethod("AdoptSessionPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(w, new object[] { "/session" });
        w.ShowState(new SessionState { Running = true });
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(rail.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "运行中" && text.IsEffectivelyVisible);
        w.ShowState(new SessionState { Terminal = new FailedTerminal("transport failed") });
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(rail.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "等你确认" && text.IsEffectivelyVisible);
        w.ShowState(new SessionState { Terminal = TurnTerminal.Completed });
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(rail.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "运行中" && text.IsEffectivelyVisible);
    });

    /// <summary>Slow remote reads cannot replace a newer search, and failed reads retain usable rows.</summary>
    /// <returns>The ordered remote refreshes.</returns>
    [Fact]
    public Task SidebarRemoteRefreshRejectsStaleReadsAndRecovers() => WithSettings("conversation", (w, reads) =>
    {
        var slow = new TaskCompletionSource<HttpResponseMessage?>();
        var unavailable = false;
        var title = "new answer";
        reads.Exchange = request =>
        {
            if (request.RequestUri!.AbsolutePath != "/remotes/gpu-box/tree")
            {
                return Task.FromResult<HttpResponseMessage?>(null);
            }

            if (request.RequestUri.Query.Contains("old", StringComparison.Ordinal))
            {
                return slow.Task;
            }

            return Task.FromResult<HttpResponseMessage?>(unavailable
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") }
                : JsonReply(Tree(title)));
        };
        var query = w.FindControl<TextBox>("RailSearch")!;
        query.Text = "old";
        query.Text = "new";
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(Labels(), text => text == "new answer");
        slow.SetResult(JsonReply(Tree("old answer")));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(Labels(), text => text == "new answer");
        Assert.DoesNotContain(Labels(), text => text == "old answer");
        unavailable = true;
        query.Text = "new ";
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(Labels(), text => text == "new answer");
        unavailable = false;
        title = "new recovered";
        query.Text = "new  ";
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(Labels(), text => text == title);

        string?[] Labels() => w.FindControl<ProjectRail>("Projects")!.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToArray();
        static JsonNode Tree(string name) => new JsonArray(new JsonObject
        {
            ["root"] = "/remote",
            ["name"] = "project",
            ["sessions"] = new JsonArray(new JsonObject { ["path"] = "/remote/session", ["title"] = name }),
        });
    });
}
