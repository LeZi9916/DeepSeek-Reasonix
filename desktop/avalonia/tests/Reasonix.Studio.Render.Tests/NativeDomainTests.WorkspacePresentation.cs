using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.Shell.Workspace;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>The session menu retains grouped actions, compact typography and both export formats on one row.</summary>
    /// <returns>The mounted menu appearance.</returns>
    [Fact]
    public Task SidebarSessionMenuUsesReferenceGroupsAndInlineExport() => WithSettings("conversation", (w, reads) =>
    {
        var rail = w.FindControl<ProjectRail>("Projects")!;
        rail.Show(reads.Read<TreeWorkspace[]>("/tree"), null);
        Dispatcher.UIThread.RunJobs();
        var row = rail.GetVisualDescendants().OfType<Button>().First(button => AutomationProperties.GetAutomationId(button) == "action:session.open");
        var path = AutomationProperties.GetHelpText(row);
        var menu = Assert.IsType<MenuFlyout>(row.ContextFlyout);
        Assert.Equal(4, menu.Items.OfType<Separator>().Count());
        menu.ShowAt(row);
        Dispatcher.UIThread.RunJobs();
        var item = menu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "action:session.pin");
        Assert.Equal(11, item.FontSize);
        var export = menu.Items.OfType<MenuItem>().Single(item => item.Header is Grid grid && grid.Children.OfType<Button>().Count() == 2);
        Assert.Equal(new[] { "Markdown", "JSON" }, Assert.IsType<Grid>(export.Header).Children.OfType<Button>().Select(button => button.Content));
        var delete = menu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "action:session.delete");
        Assert.NotEqual(Assert.IsAssignableFrom<ISolidColorBrush>(item.Foreground).Color, Assert.IsAssignableFrom<ISolidColorBrush>(delete.Foreground).Color);
        WorkspaceAction? picked = null;
        rail.ActionRequested += action => picked = action;
        var markdown = Assert.IsType<Grid>(export.Header).Children.OfType<Button>().First();
        var popup = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(markdown));
        var point = markdown.TranslatePoint(new Point(markdown.Bounds.Width / 2, markdown.Bounds.Height / 2), popup)!.Value;
        popup.MouseMove(point);
        popup.MouseDown(point, MouseButton.Left);
        popup.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("session.export-md", picked?.Action);
        row = rail.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:session.open" && AutomationProperties.GetHelpText(button) == path);
        menu = Assert.IsType<MenuFlyout>(row.ContextFlyout);
        menu.ShowAt(row);
        Dispatcher.UIThread.RunJobs();
        export = menu.Items.OfType<MenuItem>().Single(item => item.Header is Grid grid && grid.Children.OfType<Button>().Count() == 2);
        var json = Assert.IsType<Grid>(export.Header).Children.OfType<Button>().Last();
        PumpNavigation(() => TopLevel.GetTopLevel(json)?.IsVisible == true && json.Focus());
        Assert.True(json.IsKeyboardFocusWithin);
        var currentPopup = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(json));
        currentPopup.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        currentPopup.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("session.export", picked?.Action);
        menu.Hide();
    });

    /// <summary>The community card uses the existing bordered actions and mono contributor pills.</summary>
    /// <returns>The actual mounted native component.</returns>
    [Fact]
    public Task CommunityUsesReferenceActionAndContributorStyles() => WithSettings("conversation", (w, _) =>
    {
        var community = new CommunityControl();
        community.Configure(InterfaceLanguage.En, _ => Task.CompletedTask);
        Assert.IsType<Grid>(w.FindControl<LayoutTransformControl>("ScaledWindow")!.Child).Children.Add(community);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(185, community.FindControl<Grid>("Join")!.Bounds.Height);
        var join = community.FindControl<Button>("JoinQq")!;
        Assert.Equal(new Thickness(1), join.BorderThickness);
        Assert.Equal(13, join.FontSize);
        Assert.Equal(FontWeight.Bold, join.FontWeight);
        var person = community.FindControl<WrapPanel>("People")!.Children.OfType<Button>().First();
        Assert.Equal(new Thickness(1), person.BorderThickness);
        Assert.Equal(12, person.FontSize);
        Assert.Equal(new CornerRadius(999), person.CornerRadius);
    });
}
