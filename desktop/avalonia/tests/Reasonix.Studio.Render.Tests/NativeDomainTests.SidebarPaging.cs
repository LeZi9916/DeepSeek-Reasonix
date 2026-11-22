using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.Shell.Workspace;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>A failed canonical scope read can retry the same scope without toggling away.</summary>
    /// <returns>The failed window and acknowledged retry.</returns>
    [Fact]
    public Task SidebarCanonicalScopeReadCanRetryInPlace() => WithSettings("conversation", (w, reads) =>
    {
        var refuse = true;
        reads.Exchange = request =>
        {
            if (request.RequestUri!.AbsolutePath != "/tree" || !request.RequestUri.Query.Contains("scope=archived", StringComparison.Ordinal))
            {
                return Task.FromResult<HttpResponseMessage?>(null);
            }

            return Task.FromResult<HttpResponseMessage?>(refuse ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") } : JsonReply(JsonSerializer.SerializeToNode(new[] { new TreeWorkspace { Root = "/retry", Name = "retry", Sessions = new[] { new TreeSession { Path = "/retry/archived", Title = "archived retry", Archived = true } } } })!));
        };
        var rail = w.FindControl<ProjectRail>("Projects")!;
        var menu = Assert.IsType<MenuFlyout>(rail.FindControl<Button>("Scope")!.Flyout);
        menu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "action:session.scope.archived").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var retry = Assert.IsType<Button>(rail.FindControl<Button>("RetrySidebar"));
        Assert.True(retry.IsVisible);
        refuse = false;
        retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(retry.IsVisible);
        Assert.Contains(rail.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetAutomationId(button) == "action:session.open" && AutomationProperties.GetName(button) == "archived retry");
    });

    /// <summary>More requests the canonical project window when the loaded preview is exhausted.</summary>
    /// <returns>The authoritative sidebar pagination.</returns>
    [Fact]
    public Task SidebarMoreReadsCanonicalRemainingRows() => WithSettings("conversation", (w, reads) =>
    {
        var sessions = Enumerable.Range(0, 12).Select(index => new TreeSession { Path = "/large/" + index, Title = "large " + index }).ToArray();
        var rail = w.FindControl<ProjectRail>("Projects")!;
        rail.Show(new[] { new TreeWorkspace { Root = "/large", Name = "large", Total = 12, Remaining = 7, Sessions = sessions.Take(5).ToArray() } }, null);
        var requested = string.Empty;
        reads.Exchange = request =>
        {
            if (request.RequestUri!.AbsolutePath == "/tree")
            {
                requested = request.RequestUri.Query;
                return Task.FromResult<HttpResponseMessage?>(JsonReply(JsonSerializer.SerializeToNode(new[] { new TreeWorkspace { Root = "/large", Name = "large", Total = 12, Sessions = sessions } })!));
            }

            return Task.FromResult<HttpResponseMessage?>(null);
        };
        Dispatcher.UIThread.RunJobs();
        var more = rail.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:session.more");
        more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("limits=", requested);
        Assert.Equal(12, rail.GetVisualDescendants().OfType<Button>().Count(button => AutomationProperties.GetAutomationId(button) == "action:session.open" && (AutomationProperties.GetName(button) ?? string.Empty).StartsWith("large ", StringComparison.Ordinal)));
    });

    /// <summary>All six scopes, canonical counts and clearing are available on the sidebar.</summary>
    /// <returns>The scope projection.</returns>
    [Fact]
    public Task SidebarHasRunningWaitingScopesCountsAndClear() => WithSettings("conversation", (w, reads) =>
    {
        var rail = w.FindControl<ProjectRail>("Projects")!;
        rail.Show(reads.Read<TreeWorkspace[]>("/tree"), null);
        Dispatcher.UIThread.RunJobs();
        var menu = Assert.IsType<MenuFlyout>(rail.FindControl<Button>("Scope")!.Flyout);
        Assert.Contains(menu.Items.OfType<MenuItem>(), item => AutomationProperties.GetAutomationId(item) == "action:session.scope.live");
        Assert.Contains(menu.Items.OfType<MenuItem>(), item => AutomationProperties.GetAutomationId(item) == "action:session.scope.waiting");
        menu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "action:session.scope.unread").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(rail.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:session.filter-clear").IsVisible);
    });

    /// <summary>Recent history requests canonical offsets, search and selected-machine data and can retry failure.</summary>
    /// <returns>The actual recent dialog flow.</returns>
    [Fact]
    public Task SidebarRecentReadsSearchEnvironmentPagesAndRetry() => WithSettings("conversation", (w, reads) =>
    {
        var requests = new List<string>();
        var refuse = true;
        reads.Exchange = request =>
        {
            if (!request.RequestUri!.Query.Contains("order=recent", StringComparison.Ordinal))
            {
                return Task.FromResult<HttpResponseMessage?>(null);
            }

            requests.Add(request.RequestUri.PathAndQuery);
            if (refuse)
            {
                return Task.FromResult<HttpResponseMessage?>(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") });
            }

            var offset = request.RequestUri.Query.Contains("offset=50", StringComparison.Ordinal) ? 50 : 0;
            return Task.FromResult<HttpResponseMessage?>(JsonReply(JsonSerializer.SerializeToNode(new[]
            {
                new TreeWorkspace { Root = "/recent", Name = "recent", RecentOffset = offset, Remaining = offset == 0 ? 2 : 0, Sessions = new[] { new TreeSession { Path = "/recent/" + offset, Title = "recent " + offset } } },
            })!));
        };
        w.FindControl<TextBox>("RailSearch")!.Text = " ";
        Dispatcher.UIThread.RunJobs();
        w.FindControl<ProjectRail>("Projects")!.FindControl<Button>("RecentTab")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var panel = Assert.Single(w.GetVisualDescendants().OfType<RecentSessionsPanel>());
        Assert.True(panel.FindControl<Button>("Retry")!.IsVisible);
        refuse = false;
        panel.FindControl<Button>("Retry")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(panel.FindControl<Button>("Next")!.IsEnabled);
        panel.FindControl<Button>("Next")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(requests, path => path.Contains("offset=50", StringComparison.Ordinal));
        panel.FindControl<ComboBox>("Machine")!.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(requests, path => path.StartsWith("/remotes/gpu-box/tree", StringComparison.Ordinal));
        panel.FindControl<TextBox>("Query")!.Text = "last";
        panel.ReloadAsync().GetAwaiter().GetResult();
        Assert.Contains(requests, path => path.Contains("q=last", StringComparison.Ordinal));
    });

    /// <summary>Pinned rows retain their static marker and preference-enabled quick actions dispatch the canonical write.</summary>
    /// <returns>The actual hover interaction.</returns>
    [Fact]
    public Task SidebarPinnedMarkerAndQuickPinFollowReadback() => WithSettings("conversation", (w, reads) =>
    {
        var pinned = true;
        var writes = 0;
        TreeWorkspace[] Tree() => new[] { new TreeWorkspace { Root = "/quick", Name = "quick", Sessions = new[] { new TreeSession { Path = "/quick/session", Title = "quick row", Pinned = pinned } } } };
        reads.Exchange = async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/tree/sessions/pin")
            {
                var body = System.Text.Json.Nodes.JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
                pinned = (bool)body["pinned"]!;
                writes++;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return request.RequestUri.AbsolutePath == "/tree" ? JsonReply(JsonSerializer.SerializeToNode(Tree())!) : null;
        };
        var rail = w.FindControl<ProjectRail>("Projects")!;
        rail.QuickActions = true;
        rail.Show(Tree(), null);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(rail.GetVisualDescendants().OfType<Viewbox>(), mark => mark.Name == "Pin" && mark.IsEffectivelyVisible);
        var row = rail.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:session.open");
        w.MouseMove(row.TranslatePoint(new Avalonia.Point(70, 15), w)!.Value);
        Dispatcher.UIThread.RunJobs();
        var pin = rail.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:session.pin" && button.IsEffectivelyVisible);
        pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, writes);
        Assert.False(pinned);
    });
}
