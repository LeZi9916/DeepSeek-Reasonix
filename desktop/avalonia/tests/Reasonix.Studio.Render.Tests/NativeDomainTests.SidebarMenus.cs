using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.Shell.Workspace;
using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Branching skips the live checkpoint and names the actual background runtime.</summary>
    /// <returns>The runtime-scoped branch request.</returns>
    [Fact]
    public Task SidebarBranchUsesLatestFinishedCheckpoint() => WithSettings("conversation", (w, reads) =>
    {
        var body = string.Empty;
        reads.Exchange = async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/rt/r9/checkpoints")
            {
                return JsonReply(JsonNode.Parse("""[{"turn":4},{"turn":1},{"turn":3}]""")!);
            }

            if (request.RequestUri.AbsolutePath == "/rt/r9/branch/at")
            {
                body = await request.Content!.ReadAsStringAsync();
                return JsonReply(new JsonObject { ["path"] = "/branch/new" });
            }

            return null;
        };
        var rail = w.FindControl<ProjectRail>("Projects")!;
        rail.Show(new[] { new TreeWorkspace { Root = "/branch", Name = "branch", Sessions = new[] { new TreeSession { Path = "/branch/source", Title = "source", RuntimeId = "r9" } } } }, null);
        rail.ShowRun(string.Empty, "/branch/source", "running", true);
        Dispatcher.UIThread.RunJobs();
        var row = rail.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:session.open");
        var menu = Assert.IsType<MenuFlyout>(row.ContextFlyout);
        menu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "action:session.branch").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, (int)JsonNode.Parse(body)!["turn"]!);
        Assert.Contains("source", (string)JsonNode.Parse(body)!["name"]!);
    });

    /// <summary>Kept versions expand under their owner and mouse and keyboard retain the canonical target.</summary>
    /// <returns>The kept transcript navigation.</returns>
    [Fact]
    public Task SidebarKeptVersionsOpenCanonicalTranscript() => WithSettings("conversation", (w, _) =>
    {
        var rail = w.FindControl<ProjectRail>("Projects")!;
        rail.Show(new[] { new TreeWorkspace { Root = "/kept", Name = "kept", Sessions = new[] { new TreeSession { Path = "/kept/main", Title = "main", Versions = new[] { new TreeSession { Path = "/kept/version", Turns = 3 } }, Copies = new[] { new TreeSession { Path = "/kept/copy" } } } } } }, null);
        SessionOpenTarget? picked = null;
        rail.NavigateRequested += target => picked = target;
        Dispatcher.UIThread.RunJobs();
        var owner = rail.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:session.open");
        var menu = Assert.IsType<MenuFlyout>(owner.ContextFlyout);
        menu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "action:session.kept").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var child = rail.GetVisualDescendants().OfType<Button>().First(button => button.Classes.Contains("session-copy"));
        var point = child.TranslatePoint(new Point(70, 15), w)!.Value;
        w.MouseDown(point, MouseButton.Left);
        w.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new SessionOpenTarget(string.Empty, "/kept", "/kept/version"), picked);
        picked = null;
        child.Focus();
        w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        w.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new SessionOpenTarget(string.Empty, "/kept", "/kept/version"), picked);
    });

    /// <summary>Narrow chrome gathers the existing controls and restores them when room returns.</summary>
    /// <returns>The actual scaled-width transition.</returns>
    [Fact]
    public Task WorkspaceChromeMorePreservesControlsAndStatusMarks() => WithSettings("conversation", (w, _) =>
    {
        var status = w.FindControl<WorkspaceChromeStatus>("WorkspaceStatus")!;
        status.Show("gpu-box", true, 2, Reasonix.Studio.Pages.I18n.InterfaceLanguage.En);
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.FindControl<Border>("HostBadge")!.IsEffectivelyVisible);
        Assert.True(w.FindControl<Border>("IsolatedBadge")!.IsEffectivelyVisible);
        Assert.True(w.FindControl<Border>("QueuedBadge")!.IsEffectivelyVisible);
        var tools = w.FindControl<Grid>("ChromeTools")!;
        w.Width = 760;
        w.FindControl<LayoutTransformControl>("ScaledWindow")!.LayoutTransform = new ScaleTransform(1.5, 1.5);
        Dispatcher.UIThread.RunJobs();
        var more = w.FindControl<Button>("ChromeMore")!;
        Assert.True(more.IsEffectivelyVisible);
        Assert.Same(tools, Assert.IsType<Flyout>(more.Flyout).Content);
        more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(tools.IsEffectivelyVisible);
        w.FindControl<LayoutTransformControl>("ScaledWindow")!.LayoutTransform = null;
        w.Width = 1440;
        Dispatcher.UIThread.RunJobs();
        Assert.False(more.IsVisible);
        Assert.Contains(tools, w.FindControl<Grid>("ChromeGroup")!.Children);
    });
}
