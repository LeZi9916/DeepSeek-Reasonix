using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.Rail;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Batch archive blocks actual live turns and retains failed rows after canonical readback.</summary>
    /// <returns>The acknowledged bulk action.</returns>
    [Fact]
    public Task SidebarBatchArchiveBlocksLiveAndRetainsRefusedRows() => WithSettings("conversation", (w, reads) =>
    {
        var written = new List<string>();
        var tree = new[] { new TreeWorkspace { Root = "/batch", Name = "batch", Sessions = new[] { new TreeSession { Path = "/batch/a", Title = "a" }, new TreeSession { Path = "/batch/b", Title = "b" } } } };
        reads.Exchange = async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/tree/sessions/archive")
            {
                var path = (string)JsonNode.Parse(await request.Content!.ReadAsStringAsync())!["path"]!;
                written.Add(path);
                return path == "/batch/a" ? new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("{}") } : new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return request.RequestUri.AbsolutePath == "/tree" ? JsonReply(JsonSerializer.SerializeToNode(tree)!) : null;
        };
        var rail = w.FindControl<ProjectRail>("Projects")!;
        rail.Show(tree, null);
        Dispatcher.UIThread.RunJobs();
        var project = rail.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:workspace.fold");
        var menu = Assert.IsType<MenuFlyout>(project.ContextFlyout);
        menu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "action:session.selection").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        rail.FindControl<Button>("BatchAll")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        rail.ShowRun(string.Empty, "/batch/a", "running", true);
        Dispatcher.UIThread.RunJobs();
        Assert.False(rail.FindControl<Button>("BatchArchive")!.IsEnabled);
        Assert.Empty(written);
        rail.ShowRun(string.Empty, "/batch/a", string.Empty, false);
        Dispatcher.UIThread.RunJobs();
        Assert.True(rail.FindControl<Button>("BatchArchive")!.IsEnabled);
        rail.FindControl<Button>("BatchArchive")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "/batch/a", "/batch/b" }, written);
        var checks = rail.GetVisualDescendants().OfType<CheckBox>().Where(check => check.IsVisible && AutomationProperties.GetAutomationId(check) == "action:session.selection").ToArray();
        Assert.Equal(2, checks.Length);
        Assert.True(checks[0].IsChecked);
        Assert.False(checks[1].IsChecked);
    });
}
