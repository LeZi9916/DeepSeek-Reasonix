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
    /// <summary>Exercises rule editing and schedule preview in the native Agent settings.</summary>
    /// <param name="width">Window width.</param>
    /// <param name="height">Window height.</param>
    /// <param name="dark">Whether to use the dark theme.</param>
    /// <returns>The scene and verified writes to the fixture.</returns>
    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1440, 900, true)]
    [InlineData(760, 480, false)]
    [InlineData(760, 480, true)]
    public Task AgentAutomationMatchesReference(int width, int height, bool dark) => WithSettings("conversation", (w, reads) =>
    {
        w.Width = width;
        w.Height = height;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var hooks = reads.Read<JsonObject>("/hooks");
        var board = reads.Read<JsonObject>("/schedules");
        var hookWrites = 0;
        var trials = 0;
        var previewed = 0;
        var created = 0;
        JsonObject? reviewed = null;
        reads.Exchange = async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get)
            {
                return path switch
                {
                    "/hooks" => JsonReply(hooks),
                    "/schedules" => JsonReply(board),
                    "/schedules/result" => JsonReply(JsonNode.Parse("""
                        {"triggerId":"sch_a1b2c3d4/1790000000","report":"Three files changed since yesterday. Two TODOs were added in `internal/store`.","pending":[{"kind":"approval","summary":"Wanted to run a shell command","detail":"git log --stat"}],"enforcement":"tool-filter","recordedUnix":1790000260}
                        """)!.AsObject()),
                    _ => null,
                };
            }

            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject();
            switch (path)
            {
                case "/hooks":
                    hookWrites++;
                    var scope = (string?)body["scope"] == "user" ? "global" : "project";
                    var rules = hooks["hooks"]!.AsArray().OfType<JsonObject>().Where(rule => (string?)rule["scope"] != scope).Select(rule => rule.DeepClone()).ToArray();
                    hooks["hooks"] = new JsonArray(rules);
                    foreach (var rule in body["hooks"]!.AsArray().OfType<JsonObject>())
                    {
                        var stored = rule.DeepClone().AsObject();
                        stored["scope"] = scope;
                        hooks["hooks"]!.AsArray().Add(stored);
                    }

                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                case "/hooks/dry-run":
                    Assert.Equal("PreToolUse", (string?)body["event"]);
                    Assert.Equal("./scripts/guard.sh", (string?)body["command"]);
                    trials++;
                    return JsonReply(new JsonObject { ["decision"] = "block", ["exitCode"] = 2, ["stderr"] = "refusing: .env is protected", ["durationMs"] = 120, ["blocks"] = true });
                case "/schedules/preview":
                    Assert.Equal("/Users/you/src/app", (string?)body["workspace"]);
                    reviewed = body;
                    previewed++;
                    return JsonReply(JsonNode.Parse("""
                        {"firstRunUnix":1790100000,"workspace":"/Users/you/src/app","model":"deepseek/deepseek-v4-pro","prompt":"概述近期改动","perRunTokens":250000,"perRunSteps":40,"perRunWallSec":900,"perDayMax":1000000,"lifetimeTokens":3000000,"expiresAtUnix":1792700000,"confirm":"mock-confirm"}
                        """)!.AsObject());
                case "/schedules":
                    Assert.Equal("mock-confirm", (string?)body["confirm"]);
                    body.Remove("confirm");
                    Assert.True(JsonNode.DeepEquals(reviewed, body));
                    created++;
                    return JsonReply(board["schedules"]![0]!.AsObject());
                case "/schedules/pause-all":
                case "/schedules/resume-all":
                    board["pausedAll"] = path.EndsWith("/pause-all", StringComparison.Ordinal);
                    return JsonReply(board);
                case "/schedules/sch_a1b2c3d4/delete":
                    board["schedules"]!.AsArray().RemoveAt(0);
                    return JsonReply(board);
                default:
                    return null;
            }
        };

        Navigate(w, "settings-session");
        Capture("automation", "settings.automation");
        Capture("hooks", "settings.hooks");
        Capture("hooks-recipes", Named("Recipes"));
        PickScope("project");
        Capture("hooks-project", "settings.hooks");
        PickScope("user");
        ClickAt(w, "action:hooks.expert");
        Capture("hooks-expert", Named("Expert"));
        var blocking = w.GetVisualDescendants().OfType<Button>()
            .Where(button => button.IsEffectivelyVisible && AutomationProperties.GetAutomationId(button) == "action:hooks.run-once").Last();
        blocking.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, trials);
        Assert.Equal(0, hookWrites);
        var lastRule = w.GetVisualDescendants().OfType<Reasonix.Studio.Shell.Settings.HookRuleEditor>().Last();
        Capture("hooks-dry-run", lastRule);
        CaptureMenu("hooks-event-menu", lastRule.FindControl<ComboBox>("EventChoice")!, lastRule);
        ClickAt(w, "action:hooks.expert");
        var recipe = ControlAt<ToggleSwitch>(w, "action:hooks.recipe");
        recipe.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, hookWrites);

        Capture("schedules", "panels.scheduled_runs");
        Capture("schedule-list", Named("Schedules"));
        Capture("schedule-runs", Named("Runs"));
        ClickAt(w, "action:schedule.result");
        Capture("schedule-result", "panels.result");
        Capture("schedule-result-footer", Named("Pending"));
        ClickAt(w, "action:schedule.result-close");
        ClickAt(w, "action:schedule.delete-ask");
        Capture("schedule-delete-ask", Named("Schedules"));
        Assert.Equal(2, board["schedules"]!.AsArray().Count);
        ClickAt(w, "action:schedule.pause-all");
        Assert.True((bool?)board["pausedAll"]);
        Capture("schedule-paused", "panels.scheduled_runs");
        ClickAt(w, "action:schedule.resume-all");
        Assert.False((bool?)board["pausedAll"]);

        ClickAt(w, "action:schedule.new");
        Capture("schedule-form", "panels.new_scheduled_run");
        var folder = ControlAt<ComboBox>(w, "field:schedule.folder");
        var prompt = ControlAt<TextBox>(w, "field:schedule.prompt");
        Capture("schedule-form-footer", ControlAt<TextBox>(w, "field:schedule.label"));
        CaptureMenu("schedule-interval-menu", ControlAt<ComboBox>(w, "field:schedule.every"), prompt);
        CaptureMenu("schedule-folder-menu", folder, folder);
        folder.SelectedIndex = 1;
        prompt.Text = "概述近期改动";
        ControlAt<TextBox>(w, "field:schedule.label").Text = "变更简报";
        Dispatcher.UIThread.RunJobs();
        ClickAt(w, "action:schedule.preview");
        Assert.Equal(1, previewed);
        Assert.Equal(0, created);
        Capture("schedule-preview", "panels.confirm_and_create");
        Capture("schedule-preview-footer", ControlAt<Button>(w, "action:schedule.create"));
        ClickAt(w, "action:schedule.create");
        Assert.Equal(1, created);
        ClickAt(w, "action:schedule.delete");
        Assert.Single(board["schedules"]!.AsArray());

        void PickScope(string scope)
        {
            var option = w.GetVisualDescendants().OfType<RadioButton>().Single(button =>
                AutomationProperties.GetAutomationId(button) == "action:hooks.scope" && Equals(button.Tag, scope));
            option.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
        }

        Control Named(string name) => w.GetVisualDescendants().OfType<Control>().First(control => control.IsEffectivelyVisible && control.Name == name);

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
    });
}
