using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    /// <summary>The changed-settings card is based on the kernel and expands only when it has keys.</summary>
    /// <param name="count">Number of keys or a host without the endpoint.</param>
    /// <returns>The card layouts and disclosure behavior.</returns>
    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    [InlineData(-1)]
    public Task CommonSettingsShowsTheKernelsChangedKeys(int count) => WithSettings("conversation", (w, reads) =>
    {
        var keys = new[] { "ui.fold_turn_work", "desktop.max_workspaces" }.Take(count).ToArray();
        reads.Exchange = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath == "/settings-changed"
            ? count < 0
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : JsonReply(new JsonObject { ["keys"] = new JsonArray(keys.Select(key => (JsonNode?)JsonValue.Create(key)).ToArray()) })
            : null);
        Navigate(w, "settings-common");
        var page = w.FindControl<ContentControl>("SettingsContent")!;
        string?[] VisibleText() => page.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text).ToArray();

        if (count < 0)
        {
            Assert.DoesNotContain("改过的设置", VisibleText());
            return;
        }

        Assert.Contains("改过的设置", VisibleText());
        if (count == 0)
        {
            Assert.Contains("所有设置都是默认值", VisibleText());
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && AutomationProperties.GetAutomationId(b) == "action:settings.changed-toggle");
            return;
        }

        Assert.Contains("有 2 项和默认值不同", VisibleText());
        Assert.DoesNotContain(keys[0], VisibleText());
        ClickAt(w, "action:settings.changed-toggle");
        Assert.All(keys, key => Assert.Contains(key, VisibleText()));
        var scroller = w.FindControl<ScrollViewer>("SettingsScroller")!;
        scroller.Offset = new Vector(0, scroller.Extent.Height - scroller.Viewport.Height);
        Dispatcher.UIThread.RunJobs();
        var card = page.GetVisualDescendants().OfType<Border>().Single(b => Equals(b.Tag, "settings.changed_settings"));
        Assert.InRange(card.TranslatePoint(default, w)!.Value.Y + card.Bounds.Height, 834.85, 836.85);
        CaptureFlow(w, "settings-changed-expanded", resetScroll: false);
        ClickAt(w, "action:settings.changed-toggle");
        Assert.All(keys, key => Assert.DoesNotContain(key, VisibleText()));
    });

    /// <summary>The shared settings heading and index place the first card at the reference offset.</summary>
    /// <returns>The common-page layout check.</returns>
    [Fact]
    public Task CommonSettingsHeadingKeepsTheReferenceTypeAndSpacing() => WithSettings("settings-common", (w, _) =>
    {
        var heading = w.FindControl<TextBlock>("SettingsHeading")!;
        var description = w.FindControl<TextBlock>("SettingsDescription")!;
        var index = w.FindControl<Border>("SettingsIndex")!;
        var content = w.FindControl<ContentControl>("SettingsContent")!;
        Assert.Equal(22, heading.FontSize);
        Assert.EndsWith("6 项", description.Text);
        Assert.InRange(index.TranslatePoint(default, w)!.Value.Y, 164.4, 165.4);
        Assert.InRange(content.TranslatePoint(default, w)!.Value.Y, 229.4, 230.4);

        var scroller = w.FindControl<ScrollViewer>("SettingsScroller")!;
        scroller.Offset = new Vector(0, 280);
        Dispatcher.UIThread.RunJobs();
        Assert.InRange(index.TranslatePoint(default, w)!.Value.Y, 47.5, 48.5);
        var scrolled = scroller.Offset.Y;
        CaptureFlow(w, "settings-index-pinned", resetScroll: false);

        var links = w.FindControl<WrapPanel>("SettingsLinks")!;
        var point = Assert.IsType<Button>(links.Children[1]).TranslatePoint(new Point(20, 12), w)!.Value;
        w.MouseMove(point);
        w.MouseDown(point, MouseButton.Left);
        w.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.True(scroller.Offset.Y < scrolled);
        var target = content.GetVisualDescendants().OfType<Border>().Single(c => Equals(c.Tag, "safety.how_new_conversations_start"));
        Assert.True(target.TranslatePoint(default, w)!.Value.Y >= index.TranslatePoint(default, w)!.Value.Y + index.Bounds.Height);
    });

    /// <summary>Settings switches preserve pointer, keyboard and dependent-state behavior.</summary>
    /// <returns>The notification interactions.</returns>
    [Fact]
    public Task NotificationSwitchesMatchTheReferenceAndSaveThroughTheHost() => WithSettings("settings-common", (w, reads) =>
    {
        var prefs = reads.Read<JsonObject>("/notifications");
        var writes = new List<JsonObject>();
        reads.Exchange = async request =>
        {
            if (request.RequestUri!.AbsolutePath != "/notifications")
            {
                return null;
            }

            if (request.Method == HttpMethod.Put)
            {
                prefs = JsonNode.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false))!.AsObject();
                writes.Add(prefs);
            }

            return JsonReply(prefs);
        };

        var notifications = ControlAt<ToggleSwitch>(w, "action:notify.enabled");
        var sound = ControlAt<ToggleSwitch>(w, "action:notify.sound");
        var turnDone = ControlAt<ToggleSwitch>(w, "action:notify.turn-done");
        Assert.Equal(new Size(34, 20), notifications.Bounds.Size);
        Assert.Equal(new Size(34, 20), sound.Bounds.Size);
        Assert.False(turnDone.IsEffectivelyEnabled);
        Assert.Null(notifications.KnobTransitions);
        w.Classes.Add("motion");
        Assert.Equal(1, notifications.KnobTransitions?.Count);
        w.Classes.Remove("motion");

        var point = notifications.TranslatePoint(new Point(17, 10), w)!.Value;
        w.MouseMove(point);
        w.MouseDown(point, MouseButton.Left);
        w.MouseUp(point, MouseButton.Left);
        PumpNavigation(() => notifications.IsChecked == true && turnDone.IsEffectivelyEnabled);
        Assert.True((bool?)Assert.Single(writes)["enabled"]);

        Assert.True(sound.Focus());
        w.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        w.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        PumpNavigation(() => writes.Count == 2 && sound.IsEffectivelyEnabled && sound.IsChecked == true);
        Assert.True((bool?)writes[1]["sound"]);
        Assert.True((bool?)writes[1]["turnDone"]);
        Assert.NotNull(writes[1]["phone"]);
        Dispatcher.UIThread.RunJobs();
    });
}
