using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Shell.Composer;
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
    /// <summary>Exercises the approval screen and composer popup with local fixture writes.</summary>
    /// <param name="width">Reference width.</param>
    /// <param name="height">Reference height.</param>
    /// <param name="dark">Whether to render dark mode.</param>
    /// <returns>The settings and popup interaction.</returns>
    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1440, 900, true)]
    [InlineData(760, 480, false)]
    [InlineData(760, 480, true)]
    public Task SafetyApprovalsAndRulesMatchReference(int width, int height, bool dark) => WithSettings("conversation", (w, reads) =>
    {
        w.Width = width;
        w.Height = height;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var status = reads.Read<JsonObject>("/status");
        var policy = JsonNode.Parse("""
            {"mode":"ask","deny":["bash(git push:*)","file_mutation(*.env*)"],"ask":["bash(rm:*)"],"allow":["bash(go test:*)"],"askOnExternalOrigin":false,"path":"/Users/you/.reasonix/config.toml"}
            """)!.AsObject();
        var tools = reads.Read<JsonObject>("/tools").DeepClone().AsObject();
        var writes = 0;
        reads.Exchange = async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get)
            {
                return path switch
                {
                    "/permissions" => JsonReply(policy),
                    "/tools" => JsonReply(tools),
                    "/status" => JsonReply(status),
                    _ => null,
                };
            }

            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject();
            switch (path)
            {
                case "/permissions":
                    writes++;
                    foreach (var (key, value) in body)
                    {
                        policy[key] = value?.DeepClone();
                    }

                    return JsonReply(policy);
                case "/tools/disabled":
                    var target = tools["tools"]!.AsArray().Single(item => (string?)item?["name"] == (string?)body["name"]);
                    target!["disabled"] = body["disabled"]!.DeepClone();
                    return JsonReply(tools);
                case "/tool-approval-mode":
                    status["toolApprovalMode"] = body["mode"]!.DeepClone();
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                default:
                    return null;
            }
        };

        Navigate(w, "settings-tools");
        var approval = Target<RadioButton>("action:tool-approval.mode", "ask");
        Assert.True(approval.IsChecked);
        Capture("approval", "settings.approval");
        Target<RadioButton>("action:tool-approval.mode", "yolo").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("yolo", (string?)status["toolApprovalMode"]);
        Assert.True(Named("ApprovalWarning").IsEffectivelyVisible);
        Capture("approval-yolo", "settings.approval");
        approval.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Capture("rules", "settings.rules_you_spell_out");
        Capture("rules-table", Named("RuleTables"));
        var group = Target<Button>("action:permissions.rule-group", "bash");
        Click(group);
        Capture("rules-collapsed", Named("RuleTables"));
        Click(group);
        CaptureMenu("rule-level-menu", Target<ComboBox>("action:permissions.rule-level", "bash(git push:*)"), Named("RuleTables"));
        ClickAt(w, "action:permissions.toggle-add");
        var pattern = ControlAt<TextBox>(w, "field:permissions.pattern");
        Capture("rule-add", Named("AddForm"));
        CaptureMenu("rule-tool-menu", ControlAt<ComboBox>(w, "field:permissions.tool"), Named("AddForm"));
        pattern.Text = "fixture-check:*";
        Dispatcher.UIThread.RunJobs();
        Capture("rule-input", Named("AddForm"));
        CaptureMenu("rule-verdict-menu", ControlAt<ComboBox>(w, "field:permissions.verdict"), Named("AddForm"));
        ClickAt(w, "action:permissions.add-rule");
        Assert.Contains(policy["deny"]!.AsArray(), item => (string?)item == "bash(fixture-check:*)");
        var verdict = Target<ComboBox>("action:permissions.rule-level", "bash(fixture-check:*)");
        verdict.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(policy["ask"]!.AsArray(), item => (string?)item == "bash(fixture-check:*)");
        Assert.DoesNotContain(policy["deny"]!.AsArray(), item => (string?)item == "bash(fixture-check:*)");

        var switchOrigin = ControlAt<ToggleSwitch>(w, "action:permissions.ask-on-external-origin");
        Capture("external-origin", "settings.outside_text");
        policy["allow"]!.AsArray().Add("read_file(README.md)");
        switchOrigin.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True((bool?)policy["askOnExternalOrigin"]);
        Assert.Contains(policy["allow"]!.AsArray(), item => (string?)item == "read_file(README.md)");
        Assert.Equal(3, writes);

        Capture("tool-list", "ext.tools_the_model_can_use");
        Capture("tool-list-tail", Named("ToolRows").GetVisualDescendants().OfType<Border>().ElementAt(4));
        Assert.DoesNotContain(w.GetVisualDescendants().OfType<ToggleSwitch>(), control => Equals(control.Tag, "todo_write"));
        var recall = Target<ToggleSwitch>("action:tool-list.toggle", "recall");
        recall.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.True((bool?)tools["tools"]!.AsArray().Single(item => (string?)item?["name"] == "recall")!["disabled"]);

        w.FindControl<Button>("CloseSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Navigate(w, "workbench");
        var compose = w.FindControl<Control>("ComposeSurface")!;
        var trigger = w.FindControl<Button>("ApprovalMode")!;
        var model = w.FindControl<Button>("ModelButton")!;
        if (width == 760)
        {
            Assert.InRange(compose.Bounds.Width, 218, 222);
        }

        Assert.Equal(30, trigger.Bounds.Width);
        Assert.InRange(trigger.TranslatePoint(default, compose)!.Value.X + trigger.Bounds.Width, 0, compose.Bounds.Width);
        Assert.InRange(model.TranslatePoint(default, compose)!.Value.X + model.Bounds.Width, 0, compose.Bounds.Width);
        Capture("composer-default");
        ClickAt(w, "action:chrome.policy");
        var popup = w.FindControl<ComposerPopup>("PolicyPopup")!;
        Capture("composer-policy");
        var panel = w.FindControl<PolicyPanel>("PolicyPanel")!;
        panel.FindControl<Button>("YoloOption")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(popup.IsOpen);
        Assert.Contains("risk", w.FindControl<Button>("ApprovalMode")!.Classes);
        Assert.Contains("current", panel.FindControl<Button>("YoloOption")!.Classes);
        Capture("composer-policy-yolo");
        ClickAt(w, "action:chrome.policy");
        Capture("composer-yolo");

        Control Named(string name) => w.GetVisualDescendants().OfType<Control>().First(control => control.Name == name);

        T Target<T>(string action, string target)
            where T : Control => w.GetVisualDescendants().OfType<T>().Single(control =>
                AutomationProperties.GetAutomationId(control) == action && Equals(control.Tag, target));

        void CaptureMenu(string phase, ComboBox combo, Control landmark)
        {
            Scroll(landmark);
            combo.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            CaptureFlow(w, $"native-safety-{phase}-{width}x{height}", resetScroll: false);
            combo.IsDropDownOpen = false;
            Dispatcher.UIThread.RunJobs();
        }

        void Capture(string phase, object? landmark = null)
        {
            if (landmark is not null)
            {
                Scroll(landmark);
            }

            CaptureFlow(w, $"native-safety-{phase}-{width}x{height}", resetScroll: false);
        }

        void Scroll(object landmark)
        {
            var target = landmark as Control ?? w.GetVisualDescendants().OfType<Control>()
                .First(control => control.IsEffectivelyVisible && Equals(control.Tag, landmark));
            var scroll = w.FindControl<ScrollViewer>("SettingsScroller")!;
            var y = target.TranslatePoint(default, w)!.Value.Y;
            var anchor = width >= 1000 ? 120 : 151;
            scroll.Offset = new Vector(0, Math.Max(0, scroll.Offset.Y + y - anchor));
            Dispatcher.UIThread.RunJobs();
        }

        static void Click(Button button)
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }
    });

    /// <summary>Checks the reference warn accent on a permission rule row.</summary>
    /// <returns>The rule-row style check.</returns>
    [Fact]
    public Task PermissionRulesUseTheReferenceLevelAccent() => WithSettings("settings-tools", (w, _) =>
    {
        var label = w.GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == "git push:*");
        var row = label.GetVisualAncestors().OfType<Border>().First(border => border.BorderThickness.Left == 2);
        Assert.NotNull(row.BorderBrush);
        Assert.Equal(0, row.BorderThickness.Top);
        var remove = row.GetVisualDescendants().OfType<Button>().First(button =>
            AutomationProperties.GetAutomationId(button) == "action:permissions.remove-rule");
        Assert.Equal(0, remove.Opacity);
    });
}
