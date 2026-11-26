using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.Rail;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Shows both project actions on hover and keeps them reachable while the menu is open.</summary>
    /// <returns>The workspace row interaction.</returns>
    [Fact]
    public Task ProjectRowActionsStayVisibleInTheProjectMenu() => WithSettings("conversation", (window, reads) =>
    {
        var rail = window.FindControl<ProjectRail>("Projects")!;
        rail.Show(reads.Read<TreeWorkspace[]>("/tree"), null);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var fold = rail.GetVisualDescendants().OfType<Button>().First(button => AutomationProperties.GetAutomationId(button) == "action:workspace.fold");
        var row = fold.GetVisualAncestors().OfType<UserControl>().First();
        var menu = row.FindControl<Button>("Menu")!;
        var create = row.FindControl<Button>("NewSession")!;
        var actions = Assert.IsType<StackPanel>(menu.Parent);
        Assert.Equal(0, actions.Opacity);
        window.MouseMove(fold.TranslatePoint(new Point(30, 14), window)!.Value);
        Assert.Equal(1, actions.Opacity);
        Assert.Equal(28, menu.Width);
        Assert.Equal(28, create.Width);
        var point = menu.TranslatePoint(new Point(14, 14), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        var flyout = Assert.IsType<MenuFlyout>(menu.Flyout);
        Assert.True(flyout.IsOpen);
        window.MouseMove(new Point(window.Width - 20, 80));
        Assert.Equal(1, actions.Opacity);
        flyout.Hide();
        Dispatcher.UIThread.RunJobs();
        Assert.True(menu.IsFocused);
    });
}
