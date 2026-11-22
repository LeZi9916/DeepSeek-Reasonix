using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.Rail;
using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Moving chooses a known remembered project and keeps a partial source-removal acknowledgement visible.</summary>
    /// <returns>The actual selected target and retained server note.</returns>
    [Fact]
    public Task SidebarMoveToChoosesKnownProjectAndPreservesPartialResult() => WithSettings("conversation", (w, reads) =>
    {
        var posted = string.Empty;
        reads.Exchange = async request =>
        {
            if (request.RequestUri!.AbsolutePath != "/tree/sessions/move-to")
            {
                return null;
            }

            posted = await request.Content!.ReadAsStringAsync();
            return JsonReply(new JsonObject { ["originalRemoved"] = false });
        };
        var rail = w.FindControl<ProjectRail>("Projects")!;
        var row = new TreeSession { Path = "/from/session", Title = "moving conversation" };
        rail.Show(new[] { new TreeWorkspace { Root = "/from", Name = "from", Remembered = true, Sessions = new[] { row } }, new TreeWorkspace { Root = "/to", Name = "destination", Remembered = true }, new TreeWorkspace { Root = "/transient", Name = "transient" } }, null);
        rail.RequestAction(new WorkspaceAction("session.move-to-start", new SessionOpenTarget(string.Empty, "/from", row.Path), row));
        Dispatcher.UIThread.RunJobs();
        var dialog = w.GetVisualDescendants().OfType<Control>().Single(control => AutomationProperties.GetAutomationId(control) == "class:move-session");
        var choices = dialog.FindControl<ListBox>("ProjectChoices")!;
        Assert.Equal("/to", Assert.Single(choices.Items.OfType<TreeWorkspace>()).Root);
        dialog.FindControl<Button>("MoveSession")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("/to", (string)JsonNode.Parse(posted)!["target"]!);
        Assert.True(dialog.IsVisible);
        Assert.True(dialog.FindControl<TextBlock>("MoveNote")!.IsVisible);
        Assert.False(dialog.FindControl<Button>("MoveSession")!.IsEnabled);
    });
}
