using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Shell.Workbench;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>A same-runtime conversation rebind preserves its workspace editor and open file tabs.</summary>
    /// <returns>The mounted workspace state across canonical status changes.</returns>
    [Fact]
    public Task RuntimeRebindingKeepsTheWorkspaceDraftAndFileTabs() => WithSettings("workbench", (w, reads) =>
    {
        var status = reads.Read<SessionStatus>("/status");
        w.AdoptComposer(status with
        {
            SessionPath = "/first.jsonl",
        });
        Navigate(w, "workbench");
        w.FindControl<Button>("EditFile")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var editor = w.FindControl<WorkbenchFileEditor>("FileEditor")!;
        editor.Text = "workspace draft kept by the runtime";
        w.AdoptComposer(status with
        {
            SessionPath = "/rebound.jsonl",
        });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("workspace draft kept by the runtime", editor.Text);
        Assert.True(w.FindControl<Control>("WorkbenchPanel")!.IsVisible);
        Assert.Contains(w.FindControl<StackPanel>("FileTabs")!.GetVisualDescendants().OfType<Button>(), button => (button.Content as string) == "README.md");
    });

    /// <summary>Browser tabs close independently while open files retain their drafts.</summary>
    /// <returns>The mounted browser-tab interaction.</returns>
    [Fact]
    public Task WorkbenchBrowserTabsCloseWithoutClosingFiles() => WithSettings("workbench", (w, _) =>
    {
        for (var index = 0; index < 2; index++)
        {
            w.FindControl<Button>("NewSurface")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var launcher = w.FindControl<Control>("WorkbenchStart")!;
            launcher.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:workbench.launch-browser").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        var tabs = w.FindControl<StackPanel>("FileTabs")!.GetVisualDescendants().OfType<Button>().Where(button => AutomationProperties.GetAutomationId(button) == "action:workbench.browser-tab").ToArray();
        Assert.Equal(2, tabs.Length);
        var host = w.FindControl<Reasonix.Studio.Controls.StudioSurfaceHost>("ManualBrowserHost")!;
        Assert.Equal(2, host.Children.Count);
        Assert.All(host.Children, page => Assert.True(page.IsAttachedToVisualTree()));
        Assert.Single(host.Children, page => page.IsVisible);
        var pages = host.Children.ToArray();
        var menu = Assert.IsType<MenuFlyout>(tabs[0].ContextFlyout);
        menu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "action:workbench.rename-tab").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        ControlAt<TextBox>(w, "field:workspace.action").Text = "Reference Docs";
        ClickAt(w, "action:workspace.confirm");
        Dispatcher.UIThread.RunJobs();
        var renamed = w.FindControl<StackPanel>("FileTabs")!.GetVisualDescendants().OfType<Button>().Single(button => (button.Content as string) == "Reference Docs");
        Assert.Equal(pages, host.Children.ToArray());
        menu = Assert.IsType<MenuFlyout>(renamed.ContextFlyout);
        menu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "action:workbench.close-all-browsers").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(host.IsVisible);
        Assert.Empty(host.Children);
        Assert.Contains(w.FindControl<StackPanel>("FileTabs")!.GetVisualDescendants().OfType<Button>(), button => (button.Content as string) == "README.md");
    });

    /// <summary>The file menu offers the same reference and system actions as the explorer.</summary>
    /// <returns>The mounted file interaction.</returns>
    [Fact]
    public Task WorkbenchFileMenuAddsAReferenceAndShowsEditorStatus() => WithSettings("workbench", (w, _) =>
    {
        var more = w.FindControl<Button>("FileMore")!;
        more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var menu = Assert.IsType<MenuFlyout>(more.Flyout);
        var actions = menu.Items.OfType<MenuItem>().ToArray();
        Assert.Contains(actions, item => AutomationProperties.GetAutomationId(item) == "action:workbench.copy-ref");
        Assert.Contains(actions, item => AutomationProperties.GetAutomationId(item) == "action:workbench.open-file");
        actions.Single(item => AutomationProperties.GetAutomationId(item) == "action:workbench.add-to-chat").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Contains("@README.md", w.FindControl<TextBox>("Composer")!.Text);
        w.FindControl<Button>("EditFile")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var editor = w.FindControl<WorkbenchFileEditor>("FileEditor")!;
        editor.Text = "first\nsecond";
        editor.CaretOffset = editor.Text.Length;
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.FindControl<Control>("FileStatus")!.IsEffectivelyVisible);
        Assert.Equal("Markdown", w.FindControl<TextBlock>("FileLanguage")!.Text);
        Assert.Contains("2", w.FindControl<TextBlock>("FileLine")!.Text);
        Assert.True(w.FindControl<TextBlock>("FileDirty")!.IsVisible);
    });

    /// <summary>The plus button opens the real launcher and files return to the existing explorer.</summary>
    /// <returns>The mounted workbench interaction.</returns>
    [Fact]
    public Task WorkbenchLauncherAndHiddenFilesUseExistingWorkspaceActions() => WithSettings("workbench", (w, reads) =>
    {
        string? listing = null;
        reads.Exchange = request =>
        {
            if (request.RequestUri!.AbsolutePath == "/workspace/files")
            {
                listing = request.RequestUri.Query;
                return Task.FromResult<HttpResponseMessage?>(JsonReply(reads.Read<JsonObject>("/workspace/files")));
            }

            return Task.FromResult<HttpResponseMessage?>(null);
        };
        w.FindControl<Button>("NewSurface")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var launcher = w.FindControl<Control>("WorkbenchStart");
        Assert.NotNull(launcher);
        Assert.True(launcher.IsEffectivelyVisible);
        launcher.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:workbench.launch-files").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(launcher.IsVisible);
        w.FindControl<Button>("HiddenFiles")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("hidden=1", listing);
        Assert.Contains("README.md", w.FindControl<ListBox>("WorkspaceFiles")!.Items.Cast<object>().Select(item => item.ToString()));
    });
}
