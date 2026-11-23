using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks the old signed-in market navigation, metadata reuse and own preview.</summary>
    /// <param name="width">Reference viewport width.</param>
    /// <param name="dark">Reference palette.</param>
    /// <returns>The publisher views.</returns>
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(760, false)]
    [InlineData(760, true)]
    public Task MarketPublisherKeepsBrowseMineAndPublishSeparate(int width, bool dark) => WithSettings("conversation", (w, reads) =>
    {
        const string Digest = "sha256:5f0c1e9a7b3d2c4e6f8a0b1c2d3e4f5a6b7c8d9e0f1a2b3c4d5e6f7a8b9c0d1e";
        var height = width == 1440 ? 900 : 480;
        w.Width = width;
        w.Height = height;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var packages = reads.Read<JsonObject>("/market/packages")["packages"]!.AsArray().OfType<JsonObject>().ToArray();
        var mine = new JsonArray(
            Own("skill", "ship-notes", "pending", "0.1.0", "发布说明整理技能。", packages[0]),
            Own("theme", "paper-light", "active", "1.0.1", "纸面质感的浅色主题。", packages[^1], 37),
            Own("plugin", "lint-kit", "rejected", "0.2.0", "代码检查插件。", packages[1]),
            Own("theme", "night-desk", "private", "0.1.0", "只给自己用的深色主题。", packages[^1]));
        var reviews = 0;
        var mutations = 0;
        var empty = false;
        var error = false;
        reads.Exchange = async request =>
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/account":
                    return JsonReply(new JsonObject { ["signedIn"] = true, ["user"] = new JsonObject { ["handle"] = "demo" } });
                case "/market/mine":
                    if (error)
                    {
                        var response = JsonReply(new JsonObject { ["error"] = "Fixture publisher read unavailable" });
                        response.StatusCode = HttpStatusCode.ServiceUnavailable;
                        return response;
                    }

                    return JsonReply(new JsonObject { ["packages"] = empty ? new JsonArray() : mine.DeepClone() });
                case "/market/mine/plan":
                    var plan = JsonNode.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false));
                    Assert.Equal("demo/night-desk", (string?)plan?["slug"]);
                    Assert.False((bool?)plan?["replace"]);
                    reviews++;
                    return JsonReply(new JsonObject
                    {
                        ["ok"] = true,
                        ["unreviewed"] = true,
                        ["version"] = "0.1.0",
                        ["planId"] = "low:sha256:own",
                        ["contentDigest"] = Digest,
                        ["actions"] = new JsonArray(JsonNode.Parse("""{"kind":"plugin","name":"night-desk","riskLevel":"low"}""")),
                    });
                case "/market/publish" or "/market/mine/install" or "/market/mine/demo/night-desk/submit":
                    mutations++;
                    return new HttpResponseMessage(HttpStatusCode.BadRequest);
                default:
                    return null;
            }
        };

        Navigate(w, "settings-ext-market");
        var browse = Action("action:market.view", "browse");
        Assert.True(browse.IsEffectivelyVisible);
        Assert.DoesNotContain(w.GetVisualDescendants().OfType<Button>(), button =>
            button.IsEffectivelyVisible && AutomationProperties.GetAutomationId(button) == "action:market.sign-in");
        Capture("browse");
        Align(Named<Border>("AccountViews"));
        Capture("browse-views");

        Click(Action("action:market.view", "mine"));
        var list = Named<StackPanel>("MineRows");
        Assert.Equal(4, list.Children.Count);
        Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(), label => label.Text == "审核中");
        Capture("mine");
        Align(list);
        Capture("mine-list");
        Align((Control)list.Children[^1]);
        Capture("mine-tail");

        Click(Action("action:market.prepare-version", "demo/night-desk"));
        var publish = Named<Border>("PublishFrame");
        Assert.Equal("night-desk", ControlAt<TextBox>(w, "action:market.draft.name").Text);
        Assert.True(ControlAt<CheckBox>(w, "action:market.draft.visibility").IsChecked);
        Assert.True(string.IsNullOrEmpty(ControlAt<TextBox>(w, "action:market.draft.source").Text));
        Assert.True(string.IsNullOrEmpty(ControlAt<TextBox>(w, "action:market.draft.version").Text));
        Assert.False(ControlAt<Button>(w, "action:market.publish").IsEnabled);
        Align(publish);
        Capture("new-version");
        Align(ControlAt<Button>(w, "action:market.publish"));
        Capture("new-version-tail");

        Click(Action("action:market.view", "browse"));
        Click(Action("action:market.view", "publish"));
        Assert.True(string.IsNullOrEmpty(ControlAt<TextBox>(w, "action:market.draft.name").Text));
        w.FindControl<ScrollViewer>("SettingsScroller")!.Offset = default;
        Dispatcher.UIThread.RunJobs();
        Capture("publish");
        Align(Named<Border>("PublishFrame"));
        Capture("publish-form");
        Align(ControlAt<Button>(w, "action:market.publish"));
        Capture("publish-tail");
        Click(Action("action:market.draft.kind", "theme"));
        ControlAt<CheckBox>(w, "action:market.draft.visibility").IsChecked = true;
        ControlAt<TextBox>(w, "action:market.draft.source").Text = "https://example.invalid/fixture-theme";
        ControlAt<TextBox>(w, "action:market.draft.name").Text = "fixture-theme";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("保存为私有", ControlAt<Button>(w, "action:market.publish").Content);
        Align(Named<Border>("PublishFrame"));
        Capture("publish-private");
        Align(ControlAt<Button>(w, "action:market.publish"));
        Capture("publish-private-tail");

        Click(Action("action:market.view", "mine"));
        Click(Action("action:market.own-inspect", "demo/night-desk"));
        PumpNavigation(() => reviews == 1);
        var review = Named<Border>("ReviewFrame");
        Assert.Contains(review.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == "未审核 · 仅你可见");
        Align(review);
        Capture("own-preview");
        ClickAt(w, "action:market.close-review");
        Assert.True(Named<StackPanel>("MineRows").IsEffectivelyVisible);

        Click(Action("action:market.view", "browse"));
        empty = true;
        Click(Action("action:market.view", "mine"));
        Assert.Empty(Named<StackPanel>("MineRows").Children);
        Align(Named<TextBlock>("MineNotice"));
        Capture("mine-empty");
        Click(Action("action:market.view", "browse"));
        error = true;
        Click(Action("action:market.view", "mine"));
        Assert.True(ControlAt<Button>(w, "action:market.mine-retry").IsEffectivelyVisible);
        Align(Named<Border>("MineError"));
        Capture("mine-error");
        Assert.Equal(0, mutations);

        static JsonObject Own(string kind, string name, string status, string version, string summary, JsonObject seed, int installs = 0)
        {
            var package = seed.DeepClone().AsObject();
            package["kind"] = kind;
            package["name"] = name;
            package["slug"] = "demo/" + name;
            package["handle"] = "demo";
            package["status"] = status;
            package["latestVersion"] = version;
            package["summary"] = summary;
            package["installCount"] = installs;
            package.Remove("installed");
            return package;
        }

        T Named<T>(string name)
            where T : Control => w.GetVisualDescendants().OfType<T>().Single(control => control.Name == name && control.IsEffectivelyVisible);

        Button Action(string action, string value) => w.GetVisualDescendants().OfType<Button>().Single(button =>
            button.IsEffectivelyVisible && Equals(button.Tag, value) && AutomationProperties.GetAutomationId(button) == action);

        static void Click(Button button)
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        void Align(Control control)
        {
            var scroll = w.FindControl<ScrollViewer>("SettingsScroller")!;
            var y = control.TranslatePoint(default, w)!.Value.Y;
            scroll.Offset = new Vector(0, Math.Max(0, scroll.Offset.Y + y - 120));
            w.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        void Capture(string phase) => CaptureFlow(w, $"native-market-account-{phase}-{width}x{height}", resetScroll: false);
    });

    /// <summary>Checks the publisher's explicit save, review submission, and exact own-plan apply.</summary>
    /// <returns>Publisher writes sent only to the mocked host.</returns>
    [Fact]
    public Task MarketPublisherSavesSubmitsAndAppliesThroughTheAccountRoutes() => WithSettings("conversation", (w, reads) =>
    {
        const string Slug = "demo/fixture-private";
        const string Digest = "sha256:fixture-content";
        var packages = new JsonArray();
        var published = 0;
        var submits = 0;
        var plans = 0;
        var installs = 0;
        reads.Exchange = async request =>
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/account":
                    return JsonReply(new JsonObject { ["signedIn"] = true, ["user"] = new JsonObject { ["handle"] = "demo" } });
                case "/market/mine":
                    return JsonReply(new JsonObject { ["packages"] = packages.DeepClone() });
                case "/market/publish":
                    var draft = JsonNode.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false))!;
                    Assert.Equal("skill", (string?)draft["kind"]);
                    Assert.Equal("fixture-private", (string?)draft["name"]);
                    Assert.Equal("private", (string?)draft["visibility"]);
                    Assert.Equal("https://example.invalid/fixture/SKILL.md", (string?)draft["source"]);
                    Assert.Equal(new[] { "fixture", "preview" }, draft["tags"]!.AsArray().Select(tag => (string?)tag));
                    var entry = new JsonObject
                    {
                        ["slug"] = Slug,
                        ["name"] = "fixture-private",
                        ["handle"] = "demo",
                        ["kind"] = "skill",
                        ["status"] = "private",
                        ["latestVersion"] = "0.1.0",
                    };
                    packages.Add(entry);
                    published++;
                    return JsonReply(new JsonObject { ["package"] = entry.DeepClone(), ["version"] = "0.1.0" });
                case "/market/mine/demo/fixture-private/submit":
                    packages[0]!["status"] = "pending";
                    submits++;
                    return JsonReply(new JsonObject { ["package"] = packages[0]!.DeepClone() });
                case "/market/mine/plan":
                    var preview = JsonNode.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false))!;
                    Assert.Equal(Slug, (string?)preview["slug"]);
                    Assert.False((bool?)preview["replace"]);
                    plans++;
                    return JsonReply(new JsonObject
                    {
                        ["ok"] = true,
                        ["version"] = "0.1.0",
                        ["planId"] = "fixture:own",
                        ["contentDigest"] = Digest,
                        ["unreviewed"] = true,
                        ["actions"] = new JsonArray(new JsonObject { ["kind"] = "skill", ["name"] = "fixture-private", ["riskLevel"] = "low" }),
                    });
                case "/market/mine/install":
                    var apply = JsonNode.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false))!;
                    Assert.Equal(Slug, (string?)apply["slug"]);
                    Assert.Equal("0.1.0", (string?)apply["version"]);
                    Assert.Equal("fixture:own", (string?)apply["planId"]);
                    Assert.Equal(Digest, (string?)apply["digest"]);
                    Assert.Null(apply["trust"]);
                    installs++;
                    return JsonReply(new JsonObject { ["ok"] = true, ["applied"] = true });
                default:
                    return null;
            }
        };

        Navigate(w, "settings-ext-market");
        var publish = w.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Tag, "publish")
            && AutomationProperties.GetAutomationId(button) == "action:market.view");
        publish.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        ControlAt<TextBox>(w, "action:market.draft.name").Text = "fixture-private";
        ControlAt<TextBox>(w, "action:market.draft.source").Text = "https://example.invalid/fixture/SKILL.md";
        ControlAt<TextBox>(w, "action:market.draft.tags").Text = "fixture， preview";
        ControlAt<CheckBox>(w, "action:market.draft.visibility").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, published);
        ClickAt(w, "action:market.publish");
        PumpNavigation(() => published == 1);
        ClickAt(w, "action:market.publish.see-mine");
        Assert.Equal(0, submits);
        ClickAt(w, "action:market.submit");
        PumpNavigation(() => submits == 1);
        Assert.DoesNotContain(w.GetVisualDescendants().OfType<Button>(), button =>
            button.IsEffectivelyVisible && AutomationProperties.GetAutomationId(button) == "action:market.submit");
        ClickAt(w, "action:market.own-inspect");
        PumpNavigation(() => plans == 1);
        Assert.Equal(0, installs);
        ClickAt(w, "action:market.install");
        PumpNavigation(() => installs == 1);
    });
}
