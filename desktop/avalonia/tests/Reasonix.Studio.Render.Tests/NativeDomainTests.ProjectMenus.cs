using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.Rail;
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
    /// <summary>Project editors are discovered on menu open and the kernel receives the selected catalog identifier.</summary>
    /// <returns>The acknowledged native project menu operation.</returns>
    [Fact]
    public Task SidebarProjectMenuReadsDynamicEditorsAndBounds() => WithSettings("conversation", (w, reads) =>
    {
        var posted = string.Empty;
        var readsCount = 0;
        reads.Exchange = async request =>
        {
            if (request.RequestUri!.AbsolutePath != "/host/open-with")
            {
                return null;
            }

            if (request.Method == HttpMethod.Post)
            {
                posted = await request.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            readsCount++;
            return JsonReply(JsonNode.Parse("""{"targets":[{"id":"vscode","name":"Visual Studio Code","available":true},{"id":"zed","name":"Zed","available":false,"why":"inside_project"},{"id":"missing","name":"Missing","available":false}]}""")!);
        };
        var rail = w.FindControl<ProjectRail>("Projects")!;
        rail.Show(new[] { new TreeWorkspace { Root = "/editor", Name = "editor", Remembered = true }, new TreeWorkspace { Root = "/missing", Name = "missing", Missing = true, Remembered = true } }, null);
        Dispatcher.UIThread.RunJobs();
        var project = rail.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:workspace.fold" && AutomationProperties.GetName(button) == "editor");
        var menu = Assert.IsType<MenuFlyout>(project.ContextFlyout);
        menu.ShowAt(project);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, readsCount);
        var editors = menu.Items.OfType<MenuItem>().Where(item => AutomationProperties.GetAutomationId(item) == "action:workspace.open-with").ToArray();
        Assert.Equal(2, editors.Length);
        Assert.True(editors[0].IsEnabled);
        Assert.False(editors[1].IsEnabled);
        Assert.False(menu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "action:workspace.move-up").IsEnabled);
        Assert.True(menu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "action:workspace.move-down").IsEnabled);
        editors[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("vscode", (string)JsonNode.Parse(posted)!["id"]!);
        Assert.Equal("/editor", (string)JsonNode.Parse(posted)!["root"]!);
        menu.Hide();
    });

    /// <summary>Moving a remote group uses the visible neighbor order and the existing place endpoint.</summary>
    /// <returns>The canonical address-book placement.</returns>
    [Fact]
    public Task SidebarRemoteMoveUsesVisibleBookNeighbors() => WithSettings("conversation", (w, reads) =>
    {
        var posted = string.Empty;
        reads.Exchange = async request =>
        {
            if (request.RequestUri!.AbsolutePath != "/remotes/a/place")
            {
                return null;
            }

            posted = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        };
        var rail = w.FindControl<ProjectRail>("Projects")!;
        rail.ShowRemotes(new[] { new RemoteRailHost { Name = "a", Projects = new[] { new TreeWorkspace { Root = "/remote-a", Name = "remote-a" } } }, new RemoteRailHost { Name = "b", Projects = new[] { new TreeWorkspace { Root = "/remote-b", Name = "remote-b" } } } });
        Dispatcher.UIThread.RunJobs();
        var project = rail.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:workspace.fold" && AutomationProperties.GetName(button) == "remote-a");
        var menu = Assert.IsType<MenuFlyout>(project.ContextFlyout);
        var moves = menu.Items.OfType<MenuItem>().Where(item => AutomationProperties.GetAutomationId(item) == "action:remote-host.move").ToArray();
        Assert.Equal(2, moves.Length);
        Assert.False(moves[0].IsEnabled);
        Assert.True(moves[1].IsEnabled);
        moves[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(string.Empty, (string)JsonNode.Parse(posted)!["before"]!);
    });
}
