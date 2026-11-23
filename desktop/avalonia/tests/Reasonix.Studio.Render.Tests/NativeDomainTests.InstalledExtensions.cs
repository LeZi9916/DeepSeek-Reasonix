using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks the installed extension groups, inline details, and contribution search.</summary>
    /// <param name="width">Reference viewport width.</param>
    /// <param name="dark">Reference palette.</param>
    /// <returns>The native extension interactions.</returns>
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(760, false)]
    [InlineData(760, true)]
    public Task InstalledExtensionsShowsAndFiltersTheThreeReferenceGroups(int width, bool dark) => WithSettings("conversation", (w, reads) =>
    {
        var height = width == 1440 ? 900 : 480;
        w.Width = width;
        w.Height = height;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var plugins = reads.Read<JsonArray>("/plugins");
        var toggles = 0;
        reads.Exchange = async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/plugins/enabled")
            {
                var update = JsonNode.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false));
                Assert.Equal("review-kit", (string?)update?["name"]);
                Assert.False((bool?)update?["enabled"]);
                plugins[0]!["enabled"] = false;
                toggles++;
                return JsonReply(new JsonObject { ["ok"] = true });
            }

            if (request.RequestUri!.AbsolutePath == "/plugins")
            {
                return JsonReply(plugins);
            }

            return null;
        };

        Navigate(w, "settings-ext");
        var body = w.FindControl<ContentControl>("SettingsContent")!;
        Assert.Contains(body.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "插件包");
        Assert.Contains(body.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "外部服务");
        Assert.Contains(body.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "技能");
        var installed = body.GetVisualDescendants().OfType<Button>().Single(button =>
            AutomationProperties.GetAutomationId(button) == "action:extensions.tab" && Equals(button.Tag, "installed"));
        Assert.True(installed.IsEffectivelyEnabled);
        Capture("installed");
        ClickAt(w, "action:extensions.add");
        var add = Action("action:extensions.add.source");
        Assert.True(add.IsEffectivelyVisible);
        Capture("add-menu");
        add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(ControlAt<TextBox>(w, "action:extensions.source").IsEffectivelyVisible);
        Align("settings.plugin_packages", 120);
        Capture("add-source");
        ClickAt(w, "action:extensions.add.cancel");
        ClickAt(w, "action:extensions.add");
        Click(Action("action:extensions.add.service"));
        Assert.True(ControlAt<TextBox>(w, "action:mcp.source").IsEffectivelyVisible);
        Align("ext.external_services", 120);
        Capture("add-service");
        ClickAt(w, "action:mcp.add.cancel");

        Align("settings.plugin_packages", 120);
        Capture("installed-list");
        var review = Action("action:extensions.expand", "review-kit");
        Click(review);
        Assert.Contains(body.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == "/review-kit:risk");
        Capture("package-open");
        var query = ControlAt<TextBox>(w, "action:extensions.search");
        query.Text = "risk";
        Dispatcher.UIThread.RunJobs();
        Assert.True(review.IsEffectivelyVisible);
        Assert.DoesNotContain(body.GetVisualDescendants().OfType<Button>(), item => Equals(item.Tag, "notion-bridge")
            && AutomationProperties.GetAutomationId(item) == "action:extensions.expand" && item.IsEffectivelyVisible);
        Capture("package-search");
        ClickAt(w, "action:extensions.clear-search");
        Assert.True(Action("action:extensions.expand", "notion-bridge").IsEffectivelyVisible);
        var toggle = body.GetVisualDescendants().OfType<ToggleSwitch>().Single(item =>
            Equals(item.Tag, "review-kit") && AutomationProperties.GetAutomationId(item) == "action:extension.enabled");
        toggle.IsChecked = false;
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpNavigation(() => toggles == 1);
        Assert.False(toggle.IsChecked);

        var service = Action("action:extensions.expand", "time");
        var scroll = w.FindControl<ScrollViewer>("SettingsScroller")!;
        scroll.Offset = new Vector(0, scroll.Offset.Y + service.TranslatePoint(default, w)!.Value.Y - 220);
        Dispatcher.UIThread.RunJobs();
        Click(service);
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == "get_current_time");
        Capture("service-open");

        Align("settings.skills", 120);
        Capture("skills");
        Click(Action("action:skills.details", "release-notes"));
        Assert.Contains(body.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == "Apache-2.0");
        Capture("skill-open");
        var declarations = Action("action:skills.details", "security-review");
        Click(declarations);
        scroll.Offset = new Vector(0, scroll.Offset.Y + declarations.TranslatePoint(default, w)!.Value.Y - 200);
        Dispatcher.UIThread.RunJobs();
        Capture("skill-declarations");

        void Align(string tag, double y)
        {
            var panel = body.GetVisualDescendants().OfType<Control>().Single(item => Equals(item.Tag, tag));
            var viewport = w.FindControl<ScrollViewer>("SettingsScroller")!;
            viewport.Offset = new Vector(0, System.Math.Max(0, viewport.Offset.Y + panel.TranslatePoint(default, w)!.Value.Y - y));
            w.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        Button Action(string id, string? key = null) => w.GetVisualDescendants().OfType<Button>().Single(button =>
            button.IsEffectivelyVisible && (key is null || Equals(button.Tag, key)) && AutomationProperties.GetAutomationId(button) == id);

        static void Click(Button button)
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        void Capture(string state) => CaptureFlow(w, $"native-extensions-{state}-{width}x{height}", resetScroll: false);
    });

    /// <summary>Inspects pasted sources before applying either native install action.</summary>
    /// <returns>The reviewed local package and external-service flows.</returns>
    [Fact]
    public Task InstalledExtensionsAppliesOnlyReviewedSourceAndService() => WithSettings("conversation", (w, reads) =>
    {
        var plans = 0;
        var packageInstalls = 0;
        var parsed = 0;
        var serviceInstalls = 0;
        reads.Exchange = async request =>
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/plugins/plan":
                    plans++;
                    return JsonReply(JsonNode.Parse("""{"ok":true,"planId":"fixture-package-plan","actions":[{"kind":"skill","name":"fixture-skill","riskLevel":"low"}]}""")!);
                case "/plugins/install":
                    var package = JsonNode.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false));
                    Assert.Equal("fixture-package-plan", (string?)package?["planId"]);
                    packageInstalls++;
                    return JsonReply(new JsonObject { ["ok"] = true, ["applied"] = true });
                case "/mcp/parse":
                    parsed++;
                    return JsonReply(JsonNode.Parse("""{"servers":[{"name":"fixture-service","transport":"http","url":"https://service.example.invalid/mcp","displayUrl":"https://service.example.invalid/mcp"}],"risks":[{"server":"fixture-service","kind":"unknown-host","field":"url"}]}""")!);
                case "/mcp/install":
                    var service = JsonNode.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false));
                    Assert.Equal("user", (string?)service?["scope"]);
                    Assert.Equal("fixture-service", (string?)service?["server"]?["name"]);
                    serviceInstalls++;
                    return JsonReply(new JsonObject { ["name"] = "fixture-service", ["state"] = "ready" });
                default:
                    return null;
            }
        };

        Navigate(w, "settings-ext");
        ClickAt(w, "action:extensions.add");
        ClickAt(w, "action:extensions.add.source");
        ControlAt<TextBox>(w, "action:extensions.source").Text = "https://packages.example.invalid/fixture";
        Dispatcher.UIThread.RunJobs();
        ClickAt(w, "action:extensions.inspect");
        PumpNavigation(() => plans == 1);
        Assert.Equal(0, packageInstalls);
        ClickAt(w, "action:extensions.install");
        PumpNavigation(() => packageInstalls == 1);
        ClickAt(w, "action:extensions.add.cancel");

        ClickAt(w, "action:extensions.add");
        ClickAt(w, "action:extensions.add.service");
        ControlAt<TextBox>(w, "action:mcp.source").Text = "https://service.example.invalid/mcp";
        Dispatcher.UIThread.RunJobs();
        ClickAt(w, "action:mcp.inspect");
        PumpNavigation(() => parsed == 1);
        Assert.Equal(0, serviceInstalls);
        ClickAt(w, "action:mcp.add");
        PumpNavigation(() => serviceInstalls == 1);
    });
}
