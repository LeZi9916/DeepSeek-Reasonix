using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Shows project overrides, unavailable sandbox support and withdrawable approval grants.</summary>
    /// <param name="width">The captured viewport width.</param>
    /// <param name="height">The captured viewport height.</param>
    /// <param name="dark">Whether the fixture uses dark mode.</param>
    /// <returns>The rendering and permission interactions.</returns>
    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1440, 900, true)]
    [InlineData(760, 480, false)]
    [InlineData(760, 480, true)]
    public Task SafetyHostOverridesMatchReference(int width, int height, bool dark) => WithSettings("conversation", (w, reads) =>
    {
        w.Width = width;
        w.Height = height;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var policy = reads.Read<JsonObject>("/permissions");
        policy["askOnExternalOrigin"] = true;
        policy["shadowedBy"] = "/Users/you/code/site/.reasonix/config.toml";
        policy["ask"]!.AsArray().Add("legacy_fixture(check:*)");
        policy["dormant"] = JsonNode.Parse("""[{"list":"ask","rule":"legacy_fixture(check:*)","tool":"legacy_fixture"}]""");
        policy["remembered"] = new JsonArray("edit_file(notes/*.md)");
        policy["rememberedPath"] = "/Users/you/code/site/.reasonix/remembered.json";
        policy["granted"] = new JsonArray("bash(fixture-check:*)");
        var sandbox = reads.Read<JsonObject>("/sandbox");
        sandbox["available"] = false;
        sandbox["effectiveBash"] = "off";
        var browser = reads.Read<JsonObject>("/browser-tools");
        browser["enabled"] = true;
        browser["effective"] = false;
        reads.Exchange = async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get)
            {
                return path switch
                {
                    "/permissions" => JsonReply(policy),
                    "/sandbox" => JsonReply(sandbox),
                    "/browser-tools" => JsonReply(browser),
                    _ => null,
                };
            }

            var rule = (string?)JsonNode.Parse(await request.Content!.ReadAsStringAsync())?["rule"];
            switch (path)
            {
                case "/permissions/remembered/revoke":
                    Assert.Equal("edit_file(notes/*.md)", rule);
                    policy["remembered"] = new JsonArray();
                    return JsonReply(policy);
                case "/permissions/revoke":
                    Assert.Equal(string.Empty, rule);
                    policy["granted"] = new JsonArray();
                    return JsonReply(policy);
                default:
                    return null;
            }
        };

        Navigate(w, "settings-tools");
        var external = ControlAt<ToggleSwitch>(w, "action:permissions.ask-on-external-origin");
        Assert.True(external.IsChecked);
        Capture("rules-inherited", "settings.rules_you_spell_out");
        var remembered = ControlAt<Button>(w, "action:permissions.revoke-remembered");
        Capture("rules-remembered", remembered);
        Capture("rules-session-granted", Named("SessionGrants"));
        Capture("external-origin-on", "settings.outside_text");
        var confined = w.GetVisualDescendants().OfType<RadioButton>().Single(control =>
            AutomationProperties.GetAutomationId(control) == "action:sandbox.mode" && Equals(control.Tag, "enforce"));
        Assert.False(confined.IsEffectivelyEnabled);
        Assert.False(Named("NetworkRow").IsEffectivelyVisible);
        Capture("sandbox-unavailable", "safety.how_commands_run");
        Assert.True(Named("BrowserStatus").IsEffectivelyVisible);
        Capture("browser-effective-off", "common.built_in_browser");

        ClickAt(w, "action:permissions.revoke-remembered");
        Assert.Empty(policy["remembered"]!.AsArray());
        ClickAt(w, "action:permissions.revoke-session");
        Assert.Empty(policy["granted"]!.AsArray());

        Control Named(string name) => w.GetVisualDescendants().OfType<Control>().First(control => control.Name == name);

        void Capture(string phase, object location)
        {
            var target = location as Control ?? w.GetVisualDescendants().OfType<Control>()
                .First(control => control.IsEffectivelyVisible && Equals(control.Tag, location));
            var scroll = w.FindControl<ScrollViewer>("SettingsScroller")!;
            var offset = target.TranslatePoint(default, w)!.Value.Y - (width >= 1000 ? 120 : 151);
            scroll.Offset = new Vector(0, Math.Max(0, scroll.Offset.Y + offset));
            Dispatcher.UIThread.RunJobs();
            CaptureFlow(w, $"native-safety-{phase}-{width}x{height}", resetScroll: false);
            if (phase.StartsWith("browser-", StringComparison.Ordinal))
            {
                CaptureFlow(w, $"native-safety-{phase}-{width}x{height}-panel", resetScroll: false, region: target);
            }
        }
    });
}
