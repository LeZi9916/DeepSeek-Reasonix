using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.Shell.Settings;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks that saving a connection writes its form and probing uses the saved machine.</summary>
    /// <returns>The native interaction.</returns>
    [Fact]
    public Task RemoteBookSavesProbesAndRemoves() => WithSettings(
        "settings-network",
        (w, reads) =>
        {
            var book = new JsonArray();
            var probes = 0;
            reads.Exchange = async req =>
            {
                var path = req.RequestUri!.AbsolutePath;
                if (path == "/remotes")
                {
                    if (req.Method == HttpMethod.Post)
                    {
                        var entry = JsonNode.Parse(await req.Content!.ReadAsStringAsync())!;
                        Assert.Equal("buildbox", (string?)entry["name"]);
                        Assert.Equal("lab.example.invalid", (string?)entry["host"]);
                        book.Add(entry);
                    }

                    return JsonReply(book);
                }

                if (path == "/remotes/buildbox/probe")
                {
                    probes++;
                    return JsonReply(new JsonObject { ["ready"] = true, ["os"] = "linux" });
                }

                if (path == "/remotes/remove")
                {
                    Assert.Equal("buildbox", (string?)JsonNode.Parse(await req.Content!.ReadAsStringAsync())?["name"]);
                    book.Clear();
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }

                return null;
            };
            ClickAt(w, "action:remote.add");
            ControlAt<TextBox>(w, "field:remote.name").Text = "buildbox";
            ControlAt<TextBox>(w, "field:remote.host").Text = "lab.example.invalid";
            AddDirectory("/srv/build");
            AddDirectory("/srv/test");
            ClickAt(w, "action:remote.directory.default");
            AddDirectory("/srv/temporary");
            var lastRemove = w.GetVisualDescendants().OfType<Button>()
                .Last(b => b.IsEffectivelyVisible && AutomationProperties.GetAutomationId(b) == "action:remote.directory.remove");
            lastRemove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            ClickAt(w, "action:remote.save");
            Assert.Single(book);
            Assert.Equal(new[] { "/srv/test", "/srv/build" }, book[0]!["workspaces"]!.AsArray().Select(folder => (string?)folder));
            Assert.Equal(0, probes);
            ClickAt(w, "action:remote.probe");
            Assert.Equal(1, probes);
            var menu = Assert.IsType<MenuFlyout>(ControlAt<Button>(w, "action:remote.menu").Flyout);
            var remove = menu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "action:remote.remove");
            remove.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Single(book);
            ClickAt(w, "action:remote.cancel-remove");
            Assert.Single(book);
            Assert.DoesNotContain(w.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && AutomationProperties.GetAutomationId(b) == "action:remote.confirm-remove");
            remove.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Single(book);
            ClickAt(w, "action:remote.confirm-remove");
            Assert.Empty(book);

            void AddDirectory(string path)
            {
                ControlAt<TextBox>(w, "field:remote.directory").Text = path;
                Dispatcher.UIThread.RunJobs();
                ClickAt(w, "action:remote.directory.add");
            }
        });

    /// <summary>Checks the machine cards and command placement at the reference viewports.</summary>
    /// <param name="width">The viewport width.</param><param name="dark">Whether to use the dark theme.</param>
    /// <returns>The native interaction and screenshots.</returns>
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(760, false)]
    [InlineData(760, true)]
    public Task RemoteBookShowsConnectionStateAndRowMenus(int width, bool dark) => WithSettings("settings-network", (w, reads) =>
    {
        var height = width == 1440 ? 900 : 480;
        w.Width = width;
        w.Height = height;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();
        var scroller = w.FindControl<ScrollViewer>("SettingsScroller")!;
        scroller.Offset = new Vector(0, width == 1440 ? 150 : 570);
        Dispatcher.UIThread.RunJobs();
        var text = w.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text).ToArray();
        Assert.Contains("其他机器", text);
        Assert.Contains("ada@10.0.0.4", text);
        Assert.Contains("已连接", text);
        Assert.Contains("/srv/training", text);
        Assert.Contains("未连接", text);
        var menus = w.GetVisualDescendants().OfType<Button>().Where(b => AutomationProperties.GetAutomationId(b) == "action:remote.menu").ToArray();
        Assert.Equal(3, menus.Length);
        Assert.Equal(2, w.GetVisualDescendants().OfType<Button>().Count(b => b.IsEffectivelyVisible && AutomationProperties.GetAutomationId(b) == "action:remote.probe"));
        CaptureFlow(w, $"settings-remotes-list-{width}x{height}", resetScroll: false);
        var menu = Assert.IsType<MenuFlyout>(menus[0].Flyout);
        Assert.Contains(menu.Items.OfType<MenuItem>(), item => AutomationProperties.GetAutomationId(item) == "action:remote.probe");
        var pointer = menus[0].TranslatePoint(new Point(14, 14), w)!.Value;
        w.MouseMove(pointer);
        w.MouseDown(pointer, MouseButton.Left);
        w.MouseUp(pointer, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.True(menu.IsOpen);
        var presenter = menu.Items.OfType<MenuItem>().First().GetVisualAncestors().OfType<MenuFlyoutPresenter>().Single();
        CaptureFlow(w, $"settings-remotes-menu-{width}x{height}", resetScroll: false, region: presenter);
        menu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "action:remote.remove")
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(ControlAt<Button>(w, "action:remote.confirm-remove").IsVisible);
        CaptureFlow(w, $"settings-remotes-remove-{width}x{height}", resetScroll: false);
        ClickAt(w, "action:remote.cancel-remove");
        menu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "action:remote.edit")
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("gpu-box", ControlAt<TextBox>(w, "field:remote.name").Text);
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), block => block.IsEffectivelyVisible && block.Text == "/srv/eval");
        scroller.Offset = new Vector(0, width == 1440 ? 450 : 870);
        CaptureFlow(w, $"settings-remotes-edit-{width}x{height}", resetScroll: false);

        var readsAt = new List<string>();
        reads.Exchange = request =>
        {
            if (request.RequestUri!.AbsolutePath != "/remotes/gpu-box/dirs")
            {
                return Task.FromResult<HttpResponseMessage?>(null);
            }

            var path = System.Uri.UnescapeDataString(request.RequestUri.Query["?path=".Length..]);
            readsAt.Add(path);
            var listing = path switch
            {
                "/srv/training" => """{"path":"/srv/training","parent":"/srv","folders":[]}""",
                "/srv" => """{"path":"/srv","parent":"/","folders":[{"name":"eval","path":"/srv/eval"},{"name":"private","path":"/srv/private"},{"name":"training","path":"/srv/training"}]}""",
                _ => """{"path":"/srv/private","parent":"/srv","folders":[],"unlisted":true}""",
            };
            return Task.FromResult<HttpResponseMessage?>(JsonReply(JsonNode.Parse(listing)!));
        };
        ClickAt(w, "action:remote.directory.browse");
        Assert.Equal("/srv/training", Assert.Single(readsAt));
        CaptureFlow(w, $"settings-remotes-browse-{width}x{height}", resetScroll: false);
        ClickAt(w, "action:remote.directory.up");
        Assert.Equal("/srv", readsAt[1]);
        var folder = w.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible
            && AutomationProperties.GetAutomationId(b) == "action:remote.directory.open" && AutomationProperties.GetName(b) == "private");
        folder.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("/srv/private", readsAt[2]);
        ClickAt(w, "action:remote.directory.pick");
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), block => block.IsEffectivelyVisible && block.Text == "/srv/private");
    });

    /// <summary>Distinguishes sidebar visibility from host writes and imports aliases into the address book.</summary>
    /// <returns>The connected settings and sidebar flow.</returns>
    [Fact]
    public Task RemoteBookHidesSuspendsAndImportsThroughTheirOwners() => WithSettings("conversation", (w, reads) =>
    {
        var book = reads.Read<JsonArray>("/remotes");
        var writes = new List<JsonObject>();
        reads.Exchange = async request =>
        {
            if (request.RequestUri!.AbsolutePath != "/remotes")
            {
                return null;
            }

            if (request.Method == HttpMethod.Post)
            {
                var entry = JsonNode.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false))!.AsObject();
                writes.Add((JsonObject)entry.DeepClone());
                var prior = book.OfType<JsonObject>().FirstOrDefault(saved => (string?)saved["name"] == (string?)entry["name"]);
                if (prior is null)
                {
                    book.Add(entry);
                }
                else
                {
                    book[book.IndexOf(prior)] = entry;
                }
            }

            return JsonReply(book);
        };

        Navigate(w, "settings-network");
        var hide = Command("gpu-box", "hide");
        Click(hide);
        Assert.Equal("在侧栏显示", hide.Header);
        Assert.Empty(writes);

        ClickAt(w, "action:settings.close");
        var rail = w.FindControl<ProjectRail>("Projects")!;
        Assert.DoesNotContain(rail.GetVisualDescendants().OfType<TextBlock>(), part => part.IsEffectivelyVisible && part.Text == "gpu-box");
        Navigate(w, "settings-network");
        Click(Command("gpu-box", "hide"));
        ClickAt(w, "action:settings.close");
        Assert.Contains(rail.GetVisualDescendants().OfType<TextBlock>(), part => part.IsEffectivelyVisible && part.Text == "gpu-box");
        Assert.Empty(writes);

        Navigate(w, "settings-network");
        Click(Command("gpu-box", "enable"));
        Assert.True((bool?)Assert.Single(writes)["disabled"]);
        Assert.Equal("/srv/training", (string?)writes[0]["workspace"]);
        Assert.Equal(3, writes[0]["workspaces"]!.AsArray().Count);
        Assert.Equal("启用这台主机", Command("gpu-box", "enable").Header);

        ClickAt(w, "action:remotes.add");
        Assert.Equal("attic", (string?)writes[1]["name"]);
        Assert.True((bool?)writes[1]["useSSHConfig"]);
        Assert.Equal("old-laptop", ControlAt<Button>(w, "action:remotes.add").Content);
        ClickAt(w, "action:remotes.add");
        Assert.Equal("old-laptop", (string?)writes[2]["name"]);
        Assert.True((bool?)writes[2]["useSSHConfig"]);
        Assert.DoesNotContain(w.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && AutomationProperties.GetAutomationId(b) == "action:remotes.add");

        MenuItem Command(string name, string action)
        {
            var card = w.GetVisualDescendants().OfType<RemoteMachineCard>()
                .Single(c => c.FindControl<TextBlock>("Machine")!.Text == name);
            var flyout = Assert.IsType<MenuFlyout>(card.FindControl<Button>("More")!.Flyout);
            return flyout.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "action:remote." + action);
        }

        static void Click(MenuItem item)
        {
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }
    });

    /// <summary>Checks the SSH candidate chips against the reference geometry.</summary>
    /// <returns>The network settings layout check.</returns>
    [Fact]
    public Task RemoteCandidatesUseReferenceChipGeometry() => WithSettings("settings-network", (w, _) =>
    {
        Button[] Chips() => w.GetVisualDescendants().OfType<Button>()
            .Where(button => AutomationProperties.GetAutomationId(button) == "action:remotes.add").ToArray();
        PumpNavigation(() => Chips().Length == 2);
        var chips = Chips();
        Assert.Equal(76, chips[0].Bounds.Width, 3);
        Assert.Equal(112, chips[1].Bounds.Width, 3);
        Assert.Equal(22, chips[0].Bounds.Height, 3);
        var all = w.GetVisualDescendants().OfType<Button>().Single(button =>
            AutomationProperties.GetAutomationId(button) == "action:remotes.add-all");
        Assert.Equal(68, all.Bounds.Width, 3);
        Assert.Equal(23, all.Bounds.Height, 3);
    });
}
