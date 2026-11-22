using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.Shell.Workspace;
using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Recent history keeps the reference date sections, chat marks and live reducer-owned tail.</summary>
    /// <returns>The visible recent state projection.</returns>
    [Fact]
    public Task RecentSessionGroupsIconsAndLiveTailUseCanonicalRows() => WithSettings("conversation", (w, reads) =>
    {
        var rail = w.FindControl<ProjectRail>("Projects")!;
        var tree = new[]
        {
            new TreeWorkspace
            {
                Root = "/recent",
                Name = "recent",
                RecentOffset = 0,
                Sessions = new[]
                {
                    new TreeSession { Path = "/today", Title = "today", LastActiveAt = DateTimeOffset.Now.ToUnixTimeMilliseconds(), RuntimeId = "r1" },
                    new TreeSession { Path = "/earlier", Title = "earlier", LastActiveAt = DateTimeOffset.Now.AddDays(-1).ToUnixTimeMilliseconds(), RuntimeId = "r2" },
                },
            },
        };
        reads.Exchange = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath == "/tree" ? JsonReply(JsonSerializer.SerializeToNode(tree)!) : null);
        rail.Show(tree, null);
        rail.ShowRun(string.Empty, "/today", "running");
        rail.ShowRun(string.Empty, "/earlier", "halt");
        rail.FindControl<Button>("RecentTab")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var recent = Assert.Single(w.GetVisualDescendants().OfType<RecentSessionsPanel>());
        var labels = recent.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).ToArray();
        Assert.Contains(labels, text => text.Classes.Contains("recent-group") && text.Text == MessageCatalog.Get("common.today", InterfaceLanguage.Zh));
        Assert.Contains(labels, text => text.Classes.Contains("recent-group") && text.Text == MessageCatalog.Get("session.earlier", InterfaceLanguage.Zh));
        Assert.Equal(2, recent.GetVisualDescendants().OfType<Control>().Count(control => AutomationProperties.GetAutomationId(control) == "class:recent-chat"));
        Assert.Contains(labels, text => text.Classes.Contains("recent-tail") && text.Text == MessageCatalog.Get("common.running", InterfaceLanguage.Zh));
        Assert.Contains(labels, text => text.Classes.Contains("recent-tail") && text.Text == MessageCatalog.Get("session.needs_you", InterfaceLanguage.Zh));
        Assert.Contains(labels, text => text.Text == MessageCatalog.Get("session.to_choose_enter_to_open", InterfaceLanguage.Zh));
        rail.ShowRun(string.Empty, "/today", string.Empty);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(recent.GetVisualDescendants().OfType<TextBlock>(), text => text.Classes.Contains("recent-tail") && text.Text == MessageCatalog.Get("common.running", InterfaceLanguage.Zh));
    });
}
