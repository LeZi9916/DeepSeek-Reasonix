using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
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
    /// <summary>Preserves the working preferences when the narration read fails.</summary>
    /// <returns>The isolated read failure.</returns>
    [Fact]
    public Task AgentPlansRemainAvailableWhenOneReadFails() => WithSettings("conversation", (w, reads) =>
    {
        reads.Exchange = request => Task.FromResult(request.RequestUri!.AbsolutePath == "/turn-narration-settings"
            ? new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
            : null);
        Navigate(w, "settings-session");
        Assert.True(ControlAt<RadioButton>(w, "action:plan-approval.auto").IsEnabled);
        Assert.True(ControlAt<ToggleSwitch>(w, "action:ask-timeout.enabled").IsEnabled);
        Assert.True(ControlAt<ToggleSwitch>(w, "action:progress-watch.pause").IsEnabled);
        Assert.False(ControlAt<RadioButton>(w, "action:chrome.turn-narration").IsEffectivelyEnabled);
    });

    /// <summary>Walks the plans and questions settings at the reference widths.</summary>
    /// <param name="width">Window width.</param>
    /// <param name="height">Window height.</param>
    /// <param name="dark">Whether to render the dark theme.</param>
    /// <returns>The completed settings interactions.</returns>
    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1440, 900, true)]
    [InlineData(760, 480, false)]
    [InlineData(760, 480, true)]
    public Task AgentPlansPresentation(int width, int height, bool dark) => WithSettings("conversation", (w, reads) =>
    {
        w.Width = width;
        w.Height = height;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var defaults = new JsonObject
        {
            ["/plan-approval"] = JsonNode.Parse("""{"auto":false,"path":"/Users/you/.reasonix/config.toml"}"""),
            ["/ask-timeout"] = reads.Read<JsonObject>("/ask-timeout").DeepClone(),
            ["/turn-narration-settings"] = JsonNode.Parse("""{"level":"brief","path":"/Users/you/.reasonix/config.toml"}"""),
            ["/progress-watch"] = JsonNode.Parse("""{"pause":false,"rounds":20,"tokenMultiple":8,"defaultRounds":20,"defaultTokenMultiple":8}"""),
        };
        var writes = 0;
        reads.Exchange = async request =>
        {
            if (defaults[request.RequestUri!.AbsolutePath] is not JsonObject value)
            {
                return null;
            }

            if (request.Method == HttpMethod.Post)
            {
                var payload = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject();
                foreach (var field in payload)
                {
                    value[field.Key] = field.Value?.DeepClone();
                }

                writes++;
            }

            return JsonReply(value);
        };
        Navigate(w, "settings-session");
        var approval = ControlAt<RadioButton>(w, "action:plan-approval.auto");
        var timeout = ControlAt<ToggleSwitch>(w, "action:ask-timeout.enabled");
        var pause = ControlAt<ToggleSwitch>(w, "action:progress-watch.pause");
        Assert.True(approval.IsChecked);
        Assert.False(timeout.IsChecked);
        Assert.False(pause.IsChecked);
        Capture("default");
        Capture("default-progress");
        Pick("action:plan-approval.auto", "auto");
        Assert.True((bool?)defaults["/plan-approval"]!["auto"]);
        Capture("approval-auto");
        Pick("action:plan-approval.auto", "ask");
        Capture("approval-ask");
        var seconds = Set(timeout, true, "field:ask-timeout.seconds");
        Assert.Equal("300", seconds.Text);
        Capture("timeout-default");
        Commit(seconds, "75");
        Assert.Equal(75, (int?)defaults["/ask-timeout"]!["seconds"]);
        Capture("timeout-custom");
        var previous = writes;
        Commit(seconds, "9");
        Assert.Equal(previous, writes);
        Capture("timeout-invalid");
        timeout.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(seconds.IsEffectivelyVisible);
        Capture("timeout-off");
        Pick("action:chrome.turn-narration", "detailed");
        Assert.Equal("detailed", (string?)defaults["/turn-narration-settings"]!["level"]);
        Capture("narration-detailed");
        Pick("action:chrome.turn-narration", "brief");
        Capture("narration-brief");

        var rounds = Set(pause, true, "field:progress-watch.rounds");
        Assert.Equal("20", rounds.Text);
        Assert.True((bool?)defaults["/progress-watch"]!["pause"]);
        Capture("progress-pause");
        Commit(rounds, "35");
        var multiple = ControlAt<TextBox>(w, "field:progress-watch.token-multiple");
        Commit(multiple, "12");
        Assert.Equal(35, (int?)defaults["/progress-watch"]!["rounds"]);
        Assert.Equal(12, (int?)defaults["/progress-watch"]!["tokenMultiple"]);
        Capture("progress-custom");
        previous = writes;
        Commit(rounds, "0");
        Assert.Equal(previous, writes);
        Capture("progress-invalid");

        void Pick(string id, string tag)
        {
            var option = w.GetVisualDescendants().OfType<RadioButton>()
                .Single(button => AutomationProperties.GetAutomationId(button) == id && Equals(button.Tag, tag));
            option.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
        }

        TextBox Set(ToggleSwitch control, bool enabled, string id)
        {
            control.IsChecked = enabled;
            Dispatcher.UIThread.RunJobs();
            return ControlAt<TextBox>(w, id);
        }

        void Capture(string phase)
        {
            var card = Tag("settings.plans_and_questions");
            var tag = phase.Split('-')[0] switch
            {
                "timeout" => "safety.question_wait_time",
                "narration" => "settings.narration_between_tool_calls",
                "progress" => "panels.long_stretches_without_progress",
                _ => phase == "default-progress" ? "panels.long_stretches_without_progress" : "settings.plans_and_questions",
            };
            var element = Tag(tag);
            var scroll = w.FindControl<ScrollViewer>("SettingsScroller")!;
            var anchor = width >= 1000 ? 120 : 183;
            var offset = element.TranslatePoint(default, w)!.Value.Y - anchor;
            scroll.Offset = new Vector(0, Math.Max(0, scroll.Offset.Y + offset));
            Dispatcher.UIThread.RunJobs();
            var name = $"native-agent-plans-{phase}-{width}x{height}";
            CaptureFlow(w, name, resetScroll: false);
            var output = Environment.GetEnvironmentVariable("STUDIO_PARITY_OUTPUT");
            if (!string.IsNullOrEmpty(output))
            {
                CaptureFlow(w, name + "-card", resetScroll: false, region: card);
                var positions = new JsonObject();
                foreach (var key in new[] { "settings.plans_and_questions", "settings.plan_approval", "safety.question_wait_time", "settings.narration_between_tool_calls", "panels.long_stretches_without_progress" })
                {
                    var control = Tag(key);
                    var point = control.TranslatePoint(default, w)!.Value;
                    positions[key] = new JsonArray(point.X, point.Y, control.Bounds.Width, control.Bounds.Height);
                }

                System.IO.File.WriteAllText(System.IO.Path.Combine(output, name + (dark ? ".dark.json" : ".light.json")), positions.ToJsonString());
            }

            Border Tag(string key) => w.GetVisualDescendants().OfType<Border>().Single(border => Equals(border.Tag, key));
        }

        static void Commit(TextBox box, string text)
        {
            box.Text = text;
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Dispatcher.UIThread.RunJobs();
        }
    });
}
