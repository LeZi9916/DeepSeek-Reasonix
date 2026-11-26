using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Reasonix.Studio.Shell.Workbench;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Mounts browser metadata without a guest and updates the selected note after a fresh read.</summary>
    /// <returns>The workbench's passive agent-browser interaction.</returns>
    [Fact]
    public Task WorkbenchAgentBrowserTabsShowOnlyReportedMetadata() => WithSettings("conversation", (window, reads) =>
    {
        var title = "Agent \u202E tab";
        var active = true;
        var requests = 0;
        reads.Exchange = request =>
        {
            if (request.RequestUri!.AbsolutePath != "/browser/tabs")
            {
                return Task.FromResult<HttpResponseMessage?>(null);
            }

            Assert.Equal(HttpMethod.Get, request.Method);
            requests++;
            return Task.FromResult<HttpResponseMessage?>(JsonReply(new JsonArray(new JsonObject
            {
                ["target"] = "remote-tab",
                ["title"] = title,
                ["url"] = "https://example.test/agent",
                ["active"] = active,
            })));
        };
        window.FindControl<MenuItem>("WorkbenchView")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        PumpNavigation(() => Tabs().Any());
        var button = Assert.Single(Tabs());
        Assert.Equal("Agent tab", button.Content);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var note = Assert.IsType<AgentPageNote>(window.FindControl<ScrollViewer>("RenderedFile")!.Content);
        window.UpdateLayout();
        PumpNavigation(() => note.IsEffectivelyVisible);
        Assert.Equal("Agent tab", note.FindControl<TextBlock>("PageTitle")!.Text);
        Assert.Equal("https://example.test/agent", note.FindControl<SelectableTextBlock>("PageAddress")!.Text);
        Assert.True(note.FindControl<TextBlock>("PageStatus")!.IsVisible);
        Assert.DoesNotContain(note.GetVisualDescendants(), control => control is Button or NativeWebView);
        Assert.True(requests > 0);

        title = "Updated page";
        active = false;
        var workbench = window.FindControl<Border>("WorkbenchPanel")!;
        workbench.IsVisible = false;
        workbench.IsVisible = true;
        PumpNavigation(() => note.FindControl<TextBlock>("PageTitle")!.Text == title);
        Assert.False(note.FindControl<TextBlock>("PageStatus")!.IsVisible);
        Assert.Equal(title, Assert.Single(Tabs()).Content);

        System.Collections.Generic.IEnumerable<Button> Tabs() => window.GetVisualDescendants().OfType<Button>()
            .Where(tab => AutomationProperties.GetAutomationId(tab) == "action:workbench.agent-browser-tab");
    });
}
