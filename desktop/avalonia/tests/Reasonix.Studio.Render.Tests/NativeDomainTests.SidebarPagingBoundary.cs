using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>An unexpanded all-scope preview keeps only its first five rows and the current conversation.</summary>
    /// <param name="remote">Whether the current row belongs to another machine.</param><returns>The bounded native preview.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SidebarPreviewKeepsOnlyTheCurrentExtraRow(bool remote) => WithSettings("conversation", (w, _) =>
    {
        var rail = w.FindControl<ProjectRail>("Projects")!;
        var project = new TreeWorkspace { Root = "/preview", Name = "preview", Sessions = Enumerable.Range(0, 12).Select(index => new TreeSession { Path = "/preview/" + index.ToString("D2"), Title = "row " + index.ToString("D2") }).ToArray() };
        rail.Show(remote ? [] : new[] { project }, "/preview/10");
        rail.ShowRemotes(remote ? new[] { new RemoteRailHost { Name = "machine", Projects = new[] { project } } } : [], currentHost: remote ? "machine" : string.Empty);
        Dispatcher.UIThread.RunJobs();
        var titles = rail.GetVisualDescendants().OfType<Button>().Where(button => AutomationProperties.GetAutomationId(button) == "action:session.open").Select(AutomationProperties.GetName).ToArray();
        Assert.Equal(6, titles.Length);
        Assert.Contains("row 10", titles);
        Assert.DoesNotContain("row 09", titles);
        Assert.Contains(rail.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetAutomationId(button) == "action:session.more");
    });

    /// <summary>The archive scope has a separator and no misleading loaded active-window count.</summary>
    /// <returns>The actual native scope menu.</returns>
    [Fact]
    public Task SidebarArchivedScopeOmitsTheUnavailableCount() => WithSettings("conversation", (w, _) =>
    {
        var rail = w.FindControl<ProjectRail>("Projects")!;
        rail.Show(new[] { new TreeWorkspace { Root = "/active", Sessions = new[] { new TreeSession { Path = "/active/session" } } } }, null);
        var menu = Assert.IsType<MenuFlyout>(rail.FindControl<Button>("Scope")!.Flyout);
        var archive = menu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "action:session.scope.archived");
        Assert.Equal(MessageCatalog.Get("common.archived", InterfaceLanguage.Zh), AutomationProperties.GetName(archive));
        Assert.Single(Assert.IsType<Grid>(archive.Header).Children);
        Assert.Contains(menu.Items, item => item is Separator);
    });

    /// <summary>Filtered scopes show every matching loaded local and remote row.</summary>
    /// <returns>The displayed canonical matching population.</returns>
    [Fact]
    public Task SidebarNonAllScopeShowsEveryLoadedMatchingRow() => WithSettings("conversation", (w, _) =>
    {
        TreeSession[] Rows(string prefix) => Enumerable.Range(0, 12).Select(index => new TreeSession { Path = prefix + index, Title = prefix + index, Unread = true }).ToArray();
        var rail = w.FindControl<ProjectRail>("Projects")!;
        rail.Show(new[] { new TreeWorkspace { Root = "/local", Name = "local", Sessions = Rows("/local/") } }, null);
        rail.ShowRemotes(new[] { new RemoteRailHost { Name = "machine", Projects = new[] { new TreeWorkspace { Root = "/remote", Name = "remote", Sessions = Rows("/remote/") } } } });
        Dispatcher.UIThread.RunJobs();
        var menu = Assert.IsType<MenuFlyout>(rail.FindControl<Button>("Scope")!.Flyout);
        menu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "action:session.scope.unread").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(24, rail.GetVisualDescendants().OfType<Button>().Count(button => AutomationProperties.GetAutomationId(button) == "action:session.open"));
        Assert.DoesNotContain(rail.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetAutomationId(button) == "action:session.more");
    });

    /// <summary>Expanding the default 50-row preview also requests the next canonical window on that click.</summary>
    /// <returns>The authoritative 100-row readback.</returns>
    [Fact]
    public Task SidebarMoreExpandsPreviewAndRequestsRemainingOnSameClick() => WithSettings("conversation", (w, reads) =>
    {
        var sessions = Enumerable.Range(0, 100).Select(index => new TreeSession { Path = "/large/" + index, Title = "large row " + index }).ToArray();
        var rail = w.FindControl<ProjectRail>("Projects")!;
        rail.Show(new[] { new TreeWorkspace { Root = "/large", Name = "large", Remaining = 100, Total = 150, Sessions = sessions.Take(50).ToArray() } }, null);
        var requested = 0;
        reads.Exchange = request =>
        {
            if (request.RequestUri!.AbsolutePath != "/tree")
            {
                return Task.FromResult<HttpResponseMessage?>(null);
            }

            requested++;
            Assert.Contains("100", Uri.UnescapeDataString(request.RequestUri.Query));
            return Task.FromResult<HttpResponseMessage?>(JsonReply(JsonSerializer.SerializeToNode(new[] { new TreeWorkspace { Root = "/large", Name = "large", Remaining = 50, Total = 150, Sessions = sessions } })!));
        };
        Dispatcher.UIThread.RunJobs();
        rail.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:session.more").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, requested);
        Assert.Equal(100, rail.GetVisualDescendants().OfType<Button>().Count(button => AutomationProperties.GetAutomationId(button) == "action:session.open" && (AutomationProperties.GetName(button) ?? string.Empty).StartsWith("large row ", StringComparison.Ordinal)));
    });

    /// <summary>The 300-row ceiling still expands its preview and then names search as the remaining route.</summary>
    /// <returns>The bounded loaded window and explicit search note.</returns>
    [Fact]
    public Task SidebarAtWindowCapExpandsLoadedRowsAndShowsSearchCaption() => WithSettings("conversation", (w, _) =>
    {
        var rail = w.FindControl<ProjectRail>("Projects")!;
        rail.Show(new[] { new TreeWorkspace { Root = "/cap", Name = "cap", Remaining = 20, Total = 320, CountsComplete = true, Sessions = Enumerable.Range(0, 300).Select(index => new TreeSession { Path = "/cap/" + index, Title = "cap " + index }).ToArray() } }, null);
        Dispatcher.UIThread.RunJobs();
        rail.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:session.more").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(300, rail.GetVisualDescendants().OfType<Button>().Count(button => AutomationProperties.GetAutomationId(button) == "action:session.open"));
        Assert.DoesNotContain(rail.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetAutomationId(button) == "action:session.more");
        Assert.Contains(rail.GetVisualDescendants().OfType<TextBlock>(), text => text.Classes.Contains("session-window-note") && text.Text == MessageCatalog.Get("session.n_older_conversations_use_search", InterfaceLanguage.Zh, ("n", "20")));
    });
}
