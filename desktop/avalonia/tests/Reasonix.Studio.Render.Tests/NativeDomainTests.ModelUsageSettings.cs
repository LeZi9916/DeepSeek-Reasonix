using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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
    /// <summary>Checks the model-use rows and the distinction between ratio preview and commit.</summary>
    /// <param name="width">Reference viewport width.</param>
    /// <param name="dark">Reference color scheme.</param>
    /// <returns>The model settings interactions.</returns>
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(760, false)]
    [InlineData(760, true)]
    public Task ModelUsageSettingsPresentsRolesAndCommitsMaintenanceOnce(int width, bool dark) => WithSettings("conversation", (w, reads) =>
    {
        var height = width == 1440 ? 900 : 480;
        w.Width = width;
        w.Height = height;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var ratio = 0.90;
        var window = 128000;
        var ratioWrites = 0;
        var roleWrites = 0;
        var defaultWrites = 0;
        var runtimeWrites = 0;
        var selected = string.Empty;
        var planner = string.Empty;
        var catalog = reads.Read<JsonObject>("/models");
        reads.Exchange = async request =>
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/models":
                    return JsonReply(catalog);
                case "/roles" when request.Method == HttpMethod.Post:
                    var assignment = JsonNode.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false));
                    Assert.Equal("planner", (string?)assignment?["role"]);
                    Assert.Equal("auto", (string?)assignment?["effort"]);
                    planner = (string?)assignment?["ref"] ?? string.Empty;
                    roleWrites++;
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                case "/roles":
                    return JsonReply(new JsonObject
                    {
                        ["roles"] = new JsonObject { ["subagent"] = "deepseek/deepseek-flash", ["planner"] = planner },
                        ["efforts"] = new JsonObject(),
                    });
                case "/roles/overrides":
                    return JsonReply(new JsonObject());
                case "/compaction/ratio":
                    var update = JsonNode.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false));
                    ratio = (double?)update?["ratio"] ?? ratio;
                    ratioWrites++;
                    return Compaction();
                case "/compaction":
                    return Compaction();
                case "/context":
                    return JsonReply(new JsonObject { ["used"] = 24800, ["window"] = window });
                case "/model" when request.Method == HttpMethod.Post:
                    var current = JsonNode.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false));
                    selected = (string?)current?["ref"] ?? string.Empty;
                    runtimeWrites++;
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                case "/default-model" when request.Method == HttpMethod.Post:
                    var persisted = JsonNode.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false));
                    Assert.Equal(selected, (string?)persisted?["ref"]);
                    foreach (var model in catalog["models"]!.AsArray().OfType<JsonObject>())
                    {
                        model["default"] = (string?)model["ref"] == selected;
                    }

                    defaultWrites++;
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                default:
                    return null;
            }
        };

        Navigate(w, "settings-model");
        var page = w.FindControl<ContentControl>("SettingsContent")!;
        var usage = Named<StackPanel>("UsageRows");
        var heading = Named<Control>("UsageHeader");
        Assert.Equal(width == 1440, heading.IsEffectivelyVisible);
        Assert.Equal(8, usage.Children.Count);
        var primary = Picker("model.select", "default");
        Assert.Equal(140, primary.Bounds.Width);
        Assert.Equal(30, primary.Bounds.Height);
        Assert.Equal("deepseek/deepseek-v4-pro", Assert.IsType<ComboBoxItem>(primary.SelectedItem).Tag);
        var card = Assert.Single(usage.GetVisualAncestors().OfType<Border>(), border => Equals(border.Tag, "models.uses_and_context"));
        var scroller = w.FindControl<ScrollViewer>("SettingsScroller")!;
        Align(card, 120);
        Capture("usage");
        primary.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(primary.IsDropDownOpen);
        Capture("usage-dropdown");
        primary.IsDropDownOpen = false;
        var plan = Picker("roles.model", "planner");
        if (width == 1440)
        {
            ScrollToEnd();
        }
        else
        {
            Align(usage.Children[3], 108);
        }

        Capture("usage-middle");
        if (width == 760)
        {
            Align(usage.Children[5], 120);
        }

        Capture("usage-tail");

        var threshold = ControlAt<Slider>(w, "action:compaction.ratio");
        ScrollToEnd();
        Assert.Equal(0.90, threshold.Value);
        Assert.Equal("115k", Named<TextBlock>("TriggerValue").Text);
        Assert.Equal("25k", Named<TextBlock>("UsedValue").Text);
        Capture("context-default");
        var start = SliderPoint(threshold, 0.90);
        var end = SliderPoint(threshold, 0.60);
        w.MouseMove(start);
        w.MouseDown(start, MouseButton.Left);
        w.MouseMove(end);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, ratioWrites);
        Assert.Equal("60%", Named<TextBlock>("RatioValue").Text);
        Assert.Equal("115k", Named<TextBlock>("TriggerValue").Text);
        Capture("context-preview");
        w.MouseUp(end, MouseButton.Left);
        PumpNavigation(() => ratioWrites == 1 && Named<TextBlock>("TriggerValue").Text == "77k");
        Capture("context-changed");
        threshold.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Left });
        Assert.Equal(1, ratioWrites);

        plan.SelectedItem = Offered(plan, "deepseek/deepseek-flash");
        PumpNavigation(() => roleWrites == 1 && Equals(Assert.IsType<ComboBoxItem>(Picker("roles.model", "planner").SelectedItem).Tag, planner));
        primary = Picker("model.select", "default");
        primary.SelectedItem = Offered(primary, "deepseek/deepseek-flash");
        PumpNavigation(() => defaultWrites == 1 && runtimeWrites == 1);
        Assert.Equal("deepseek/deepseek-flash", selected);

        window = 0;
        Navigate(w, "settings-common");
        Navigate(w, "settings-model");
        ScrollToEnd();
        Assert.False(Named<Control>("ContextGauge").IsEffectivelyVisible);
        Assert.Equal("—", Named<TextBlock>("CapacityValue").Text);
        Capture("context-unreported");

        HttpResponseMessage Compaction() => JsonReply(new JsonObject
        {
            ["ratio"] = ratio,
            ["context_window"] = window,
            ["trigger"] = (int)(window * ratio),
        });

        ComboBox Picker(string id, string role) => page.GetVisualDescendants().OfType<ComboBox>()
            .Single(combo => AutomationProperties.GetAutomationId(combo) == "action:" + id && Equals(combo.Tag, role));

        T Named<T>(string name)
            where T : Control => page.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

        static ComboBoxItem Offered(ComboBox combo, string model) => combo.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, model));

        Point SliderPoint(Slider slider, double value)
        {
            var x = 6.5 + ((value - 0.10) / 0.89 * (slider.Bounds.Width - 13));
            return slider.TranslatePoint(new Point(x, 9), w)!.Value;
        }

        void ScrollToEnd()
        {
            scroller.Offset = new Vector(0, scroller.Extent.Height);
            Dispatcher.UIThread.RunJobs();
        }

        void Align(Control control, double yOffset)
        {
            var y = control.TranslatePoint(default, w)!.Value.Y;
            scroller.Offset = new Vector(0, System.Math.Max(0, scroller.Offset.Y + y - yOffset));
            w.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        void Capture(string state) => CaptureFlow(w, $"native-model-usage-{state}-{width}x{height}", resetScroll: false);
    });

    /// <summary>Keeps a saved threshold preview when the turn is busy and restores it after a failed save.</summary>
    /// <returns>The native saved and refused states.</returns>
    [Fact]
    public Task ModelUsageSettingsKeepsTheSavedRatioUntilTheRuntimeCanApplyIt() => WithSettings("conversation", (w, reads) =>
    {
        var writes = 0;
        reads.Exchange = request =>
        {
            if (request.RequestUri!.AbsolutePath != "/compaction/ratio")
            {
                return Task.FromResult<HttpResponseMessage?>(null);
            }

            writes++;
            var accepted = writes == 1;
            var reply = JsonReply(new JsonObject
            {
                ["code"] = accepted ? "compaction.saved_while_running" : "compaction.ratio_out_of_range",
                ["error"] = accepted ? "Saved during a running turn." : "Threshold refused.",
            });
            reply.StatusCode = accepted ? HttpStatusCode.Conflict : HttpStatusCode.BadRequest;
            return Task.FromResult<HttpResponseMessage?>(reply);
        };

        Navigate(w, "settings-model");
        var slider = ControlAt<Slider>(w, "action:compaction.ratio");
        var scope = w.FindControl<ContentControl>("SettingsContent")!;
        var trigger = Read("TriggerValue");
        var failure = Read("RatioFailure");
        slider.Value = 0.64;
        Release();
        PumpNavigation(() => failure.IsEffectivelyVisible && writes == 1);
        Assert.Equal(0.64, slider.Value);
        Assert.Equal("115k", trigger.Text);
        Assert.StartsWith("已保存，尚未生效", failure.Text);
        slider.Value = 0.50;
        Release();
        PumpNavigation(() => failure.IsEffectivelyVisible && writes == 2);
        Assert.Equal(0.64, slider.Value);
        Assert.Equal("115k", trigger.Text);

        TextBlock Read(string name) => scope.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Name == name);

        void Release()
        {
            slider.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Left });
            Dispatcher.UIThread.RunJobs();
        }
    });
}
