using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
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
    /// <summary>Exercises the collaboration settings with local peer and project fixtures.</summary>
    /// <param name="width">Window width.</param>
    /// <param name="height">Window height.</param>
    /// <param name="dark">Whether the theme is dark.</param>
    /// <returns>The mounted scene and interactions.</returns>
    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1440, 900, true)]
    [InlineData(760, 480, false)]
    [InlineData(760, 480, true)]
    public Task AgentCollaborationMatchesReference(int width, int height, bool dark) => WithSettings("conversation", (w, reads) =>
    {
        w.Width = width;
        w.Height = height;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        var reference = Parse("""{"tokens":8000,"defaultTokens":8000,"minTokens":500,"maxTokens":200000,"path":"/Users/you/.reasonix/config.toml"}""");
        var claims = Parse("""{"optimistic":true,"path":"/Users/you/.reasonix/config.toml"}""");
        var settings = Parse("""{"enabled":false,"dispatch":false,"open_nested":false,"available":true,"pending":false,"path":"/Users/you/.reasonix/config.toml","release_projects":[]}""");
        var target = Parse("""{"key":"s-1a2b3c4d","title":"拾音","project":"rx-archive"}""");
        var secondTarget = Parse("""{"key":"s-5e6f7a8b","title":"文档整理","project":"docs"}""");
        var projects = JsonNode.Parse("""
            [{"path":"/Users/you/work/site","name":"site"},{"path":"/Users/you/work/api-gateway","name":"api-gateway"}]
            """)!.AsArray();
        var remembered = JsonNode.Parse("""
            [{"root":"~/projects/DeepSeek-Reasonix","name":"DeepSeek-Reasonix","remembered":true},{"root":"~/projects/my-website","name":"my-website","remembered":true}]
            """)!.AsArray();
        var held = Parse("""{"id":"m-1","fromSession":"s-1a2b3c4d","fromTitle":"拾音","fromProject":"rx-archive","body":"迁移脚本跑完了，结果在 out/report.md，请你核对第三节再继续。"}""");
        var remotelyAdded = Parse("""{"path":"/other-window","name":"Other window"}""");
        var postedProjects = 0;
        var sends = 0;
        var resolves = 0;
        reads.Exchange = async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get)
            {
                return path switch
                {
                    "/session-ref-settings" => JsonReply(reference),
                    "/subagent-claims" => JsonReply(claims),
                    "/peers/settings" => JsonReply(settings),
                    "/tree" when request.RequestUri.Query.Contains("limit=1", StringComparison.Ordinal) => JsonReply(remembered),
                    "/peers" => JsonReply(new JsonObject
                    {
                        ["enabled"] = settings["enabled"]!.DeepClone(),
                        ["peers"] = new JsonArray(target.DeepClone(), secondTarget.DeepClone()),
                        ["held"] = resolves == 0 ? new JsonArray(held.DeepClone()) : new JsonArray(),
                    }),
                    _ => null,
                };
            }

            var edit = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject();
            switch (path)
            {
                case "/session-ref-settings":
                    reference["tokens"] = (int?)edit["tokens"] is 0 ? 8000 : edit["tokens"]!.DeepClone();
                    return JsonReply(reference);
                case "/subagent-claims":
                    claims["optimistic"] = edit["optimistic"]!.DeepClone();
                    return JsonReply(claims);
                case "/peers/settings":
                    foreach (var (key, value) in edit)
                    {
                        if (key == "release_projects")
                        {
                            postedProjects++;
                            settings[key] = new JsonArray(value!.AsArray().Select(project =>
                                new JsonObject { ["path"] = project!.DeepClone(), ["name"] = (string?)project }).ToArray());
                        }
                        else
                        {
                            settings[key] = value?.DeepClone();
                        }
                    }

                    settings["pending"] = true;
                    if ((bool?)edit["enabled"] == true)
                    {
                        settings["release_projects"] = projects.DeepClone();
                    }

                    return JsonReply(settings);
                case "/peers/send":
                    Assert.Equal("s-1a2b3c4d", (string?)edit["to"]);
                    Assert.Equal("Review complete.", (string?)edit["text"]);
                    sends++;
                    return JsonReply(new JsonObject { ["id"] = "sent", ["to"] = target.DeepClone() });
                case "/peers/held/m-1":
                    Assert.True((bool?)edit["approve"]);
                    Assert.Equal("none", (string?)edit["remember"]);
                    resolves++;
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                default:
                    return null;
            }
        };

        Navigate(w, "settings-session");
        var tokens = ControlAt<TextBox>(w, "field:session-ref.tokens");
        var reset = ControlAt<Button>(w, "action:session-ref.reset");
        Assert.Equal("8000", tokens.Text);
        Assert.False(reset.IsEffectivelyEnabled);
        Capture("collaboration", "settings.collaboration");
        tokens.Text = "12000";
        tokens.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(12000, (int?)reference["tokens"]);
        Capture("reference-edited", "composer.size_of_a_referenced_conversation");
        ClickAt(w, "action:session-ref.reset");
        Assert.Equal("8000", tokens.Text);
        Capture("reference-reset", "composer.size_of_a_referenced_conversation");

        var all = w.GetVisualDescendants().OfType<RadioButton>().Single(control =>
            AutomationProperties.GetAutomationId(control) == "action:subagent-claims.optimistic" && Equals(control.Tag, "all"));
        all.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.False((bool?)claims["optimistic"]);
        Capture("claims-all", "session.sub_agent_write_scope");
        Capture("peers-disabled", "remote.messages_between_sessions_experimental");
        Capture("peers-projects-disabled", "remote.projects_allowed_through");

        var enabled = ControlAt<ToggleSwitch>(w, "action:peers.enable");
        var dispatch = ControlAt<ToggleSwitch>(w, "action:peers.dispatch");
        var nested = ControlAt<ToggleSwitch>(w, "action:peers.open-nested");
        Assert.False(dispatch.IsEffectivelyEnabled);
        Switch(enabled, true);
        Assert.True(dispatch.IsEffectivelyEnabled);
        Assert.False(nested.IsEffectivelyEnabled);
        Switch(dispatch, true);
        Assert.True(nested.IsEffectivelyEnabled);
        Capture("peers-enabled", "remote.messages_between_sessions_experimental");
        Switch(nested, true);
        Assert.True((bool?)settings["open_nested"]);
        Capture("peers-nested", "remote.messages_between_sessions_experimental");
        Capture("peers-projects", "remote.projects_allowed_through");
        var releaseRows = w.GetVisualDescendants().OfType<StackPanel>().Single(control => control.Name == "ReleaseList");
        Capture("peers-projects-footer", releaseRows);
        CaptureMenu("peers-projects-menu", ControlAt<ComboBox>(w, "field:peers.release-pick"), "remote.projects_allowed_through");
        var replyTarget = ControlAt<ComboBox>(w, "field:peers.target");
        CaptureMenu("peers-target-menu", replyTarget, replyTarget);
        Capture("peers-held", "remote.text_from_another_session_from");
        ClickAt(w, "action:peers.approve");
        Assert.Equal(1, resolves);
        var message = ControlAt<TextBox>(w, "field:peers.text");
        message.Text = "Review complete.";
        Dispatcher.UIThread.RunJobs();
        ClickAt(w, "action:peers.send");
        Assert.Equal(1, sends);
        Assert.Equal(string.Empty, message.Text);

        settings["release_projects"]!.AsArray().Add(remotelyAdded);
        ClickAt(w, "action:peers.release-add");
        Assert.Equal(4, settings["release_projects"]!.AsArray().Count);
        Assert.Contains(settings["release_projects"]!.AsArray(), project => (string?)project?["path"] == "/other-window");
        var revoke = w.GetVisualDescendants().OfType<Button>().Single(control =>
            AutomationProperties.GetAutomationId(control) == "action:peers.release-revoke" && Equals(control.Tag, "/other-window"));
        revoke.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, postedProjects);
        Assert.Equal(3, settings["release_projects"]!.AsArray().Count);

        void CaptureMenu(string phase, ComboBox combo, object landmark)
        {
            Scroll(landmark);
            combo.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            CaptureFlow(w, $"native-agent-collaboration-{phase}-{width}x{height}", resetScroll: false);
            combo.IsDropDownOpen = false;
            Dispatcher.UIThread.RunJobs();
        }

        void Capture(string phase, object target)
        {
            Scroll(target);
            CaptureFlow(w, $"native-agent-collaboration-{phase}-{width}x{height}", resetScroll: false);
        }

        void Scroll(object target)
        {
            var landmark = target as Control ?? w.GetVisualDescendants().OfType<Control>()
                .First(control => control.IsEffectivelyVisible && Equals(control.Tag, target));
            var scroll = w.FindControl<ScrollViewer>("SettingsScroller")!;
            var y = landmark.TranslatePoint(default, w)!.Value.Y;
            scroll.Offset = new Vector(0, Math.Max(0, scroll.Offset.Y + y - (width >= 1000 ? 120 : 183)));
            Dispatcher.UIThread.RunJobs();
        }

        static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();

        static void Switch(ToggleSwitch toggle, bool on)
        {
            toggle.IsChecked = on;
            Dispatcher.UIThread.RunJobs();
        }
    });
}
