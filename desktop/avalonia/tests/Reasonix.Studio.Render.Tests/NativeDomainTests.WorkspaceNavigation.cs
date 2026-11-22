using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.Rail;
using System;
using System.Linq;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Pointer release over titles, empty space and timestamps activates the row once.</summary>
    /// <returns>The actual headless pointer route.</returns>
    [Fact]
    public Task SidebarRowsHitTestAcrossTheirWholeSurface() => WithSettings("conversation", (w, _) =>
    {
        var rail = w.FindControl<ProjectRail>("Projects")!;
        rail.Show(
            new[]
            {
            new TreeWorkspace
            {
                Root = "/project",
                Name = "project",
                Sessions = new[] { new TreeSession { Path = "/session", Title = "pointer row", LastActiveAt = DateTimeOffset.Now.AddMinutes(-12).ToUnixTimeMilliseconds() } },
            },
            },
            null);
        Dispatcher.UIThread.RunJobs();
        var pressed = 0;
        rail.NavigateRequested += target =>
        {
            Assert.Equal("/project", target.WorkspaceRoot);
            Assert.Equal("/session", target.SessionPath);
            pressed++;
        };
        var row = rail.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "action:session.open");
        foreach (var x in new[] { 50d, 145d, row.Bounds.Width - 35 })
        {
            var point = row.TranslatePoint(new Point(x, 15), w)!.Value;
            w.MouseMove(point);
            w.MouseDown(point, MouseButton.Left);
            w.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal(3, pressed);
        row.Focus();
        w.KeyPress(Key.Enter, Avalonia.Input.RawInputModifiers.None, PhysicalKey.Enter, "\r");
        w.KeyRelease(Key.Enter, Avalonia.Input.RawInputModifiers.None, PhysicalKey.Enter, "\r");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(4, pressed);
    });

    /// <summary>The row's menu exposes the same canonical session operations as Electron.</summary>
    /// <returns>The mounted row menu.</returns>
    [Fact]
    public Task SidebarSessionMenuContainsReferenceActions() => WithSettings("conversation", (w, reads) =>
    {
        var rail = w.FindControl<ProjectRail>("Projects")!;
        rail.Show(reads.Read<TreeWorkspace[]>("/tree"), null);
        Dispatcher.UIThread.RunJobs();
        var row = rail.GetVisualDescendants().OfType<Button>().First(b => AutomationProperties.GetAutomationId(b) == "action:session.open");
        var menu = Assert.IsType<MenuFlyout>(row.ContextFlyout);
        foreach (var action in new[] { "session.rename-start", "session.auto-name", "session.copy-info", "session.move-to-start", "session.delete" })
        {
            Assert.Contains(menu.Items.OfType<MenuItem>(), item => AutomationProperties.GetAutomationId(item) == "action:" + action);
        }

        Assert.Contains(menu.Items.OfType<MenuItem>(), item => item.Header is Grid grid && grid.Children.OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "action:session.export"));

        Assert.NotNull(typeof(ProjectRail).GetEvent("NavigateRequested"));
    });
}
