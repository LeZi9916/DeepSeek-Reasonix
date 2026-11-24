using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Events;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Exercises the subagent rail deck from typed dispatch and tracker events.</summary>
    /// <param name="dark">Whether to use the dark palette.</param>
    /// <returns>The subagent chip, detail and process interaction.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SubagentDeckFollowsLiveTrackerAndKeepsRowsMounted(bool dark) =>
        WithSettingsAsync(
            "conversation",
            async (w, reads) =>
            {
                w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                await WaitForMotionFrame(() => w.Classes.Contains("motion")).ConfigureAwait(true);
                var status = reads.Read<SessionStatus>("/status") with
                {
                    Jobs = [],
                    EndedJobs = [],
                };
                w.AdoptComposer(status);
                var reducer = new SessionReducer(new IdSequence());
                var state = Fold(new SessionState(), """{"kind":"turn_started","msgIndex":3,"authoredTurn":3,"text":"请并行核对两个界面状态"}""");
                state = Fold(state, """{"kind":"tool_dispatch","tool":{"id":"review-layout","name":"task","profile":{"name":"review"}}}""");
                state = Fold(state, """{"kind":"tool_dispatch","tool":{"id":"review-motion","name":"task","profile":{"name":"review"}}}""");
                w.ShowState(state);
                w.UpdateLayout();

                var chip = w.FindControl<UserControl>("Subagents")!;
                var processes = w.FindControl<UserControl>("Processes")!;
                var trigger = chip.FindControl<Button>("Trigger")!;
                var pulse = chip.FindControl<Control>("Pulse")!;
                var deck = chip.FindControl<StudioHoverCard>("Details")!;
                var frame = deck.FindControl<Border>("Frame")!;
                var rows = chip.FindControl<StackPanel>("Rows")!;
                var root = w.FindControl<Grid>("LayoutRoot")!;
                var phase = AvaloniaPropertyRegistry.Instance.FindRegistered(pulse, "Phase")!;
                var capture = Environment.GetEnvironmentVariable("STUDIO_MOTION_OUTPUT");
                var scheme = dark ? "dark" : "light";
                Assert.True(chip.IsEffectivelyVisible && processes.IsEffectivelyVisible);
                Assert.Equal(22, trigger.Bounds.Height);
                Assert.Equal(6, pulse.Bounds.Width);
                Assert.Equal("2", chip.FindControl<TextBlock>("Count")!.Text);
                Assert.Equal("个子代理在跑", chip.FindControl<TextBlock>("Caption")!.Text);

                if (!string.IsNullOrEmpty(capture))
                {
                    foreach (var (target, min, max) in new[] { (0, 0d, 0.055), (340, 0.39, 0.52), (800, 0.85, 0.92), (1120, 1d, 1d) })
                    {
                        await WaitForMotionFrame(() => pulse.IsAnimating(phase) && Progress() >= min && Progress() <= max).ConfigureAwait(true);
                        CaptureMotionRegion(w, root, 1032, -864, 147, 36, $"subagent-pulse-{scheme}-{target}");
                    }
                }

                Click(trigger);
                Assert.True(deck.IsOpen);
                if (!string.IsNullOrEmpty(capture))
                {
                    await WaitForMotionFrame(() => frame.Opacity is > 0.69 and < 0.94).ConfigureAwait(true);
                    Assert.Equal(4 * (1 - frame.Opacity), frame.RenderTransform!.Value.M32, 5);
                    CaptureMotionRegion(w, root, 819, -703, 364, 197, $"subagent-deck-{scheme}-35");
                }

                await WaitForMotionFrame(() => frame.Opacity == 1).ConfigureAwait(true);
                w.UpdateLayout();
                Assert.Equal(340, frame.Bounds.Width);
                Assert.Equal(trigger.TranslatePoint(default, w)!.Value.X + trigger.Bounds.Width, frame.TranslatePoint(default, w)!.Value.X + frame.Bounds.Width, 1);
                CaptureMotionRegion(w, root, 819, -703, 364, 197, $"subagent-deck-{scheme}-120");

                if (!string.IsNullOrEmpty(capture))
                {
                    var chipPosition = trigger.TranslatePoint(default, w)!.Value;
                    var cardPosition = frame.TranslatePoint(default, w)!.Value;
                    File.WriteAllText(
                        Path.Combine(capture, $"native-subagent-deck-{scheme}-geometry.txt"),
                        FormattableString.Invariant($"chip={chipPosition.X:F3},{chipPosition.Y:F3},{trigger.Bounds.Width:F3},{trigger.Bounds.Height:F3} card={cardPosition.X:F3},{cardPosition.Y:F3},{frame.Bounds.Width:F3},{frame.Bounds.Height:F3}"));
                    deck.Close();
                    var popup = deck.FindControl<Popup>("Surface")!;
                    Assert.True(popup.IsOpen);
                    Assert.False(frame.IsHitTestVisible);
                    await WaitForMotionFrame(() => frame.Opacity is > 0.05 and < 0.42).ConfigureAwait(true);
                    CaptureMotionRegion(w, root, 819, -703, 364, 197, $"subagent-deck-close-{scheme}-35");
                    await WaitForMotionFrame(() => !popup.IsOpen).ConfigureAwait(true);
                    CaptureMotionRegion(w, root, 819, -703, 364, 197, $"subagent-deck-close-{scheme}-120");
                    Click(trigger);
                    await WaitForMotionFrame(() => frame.Opacity == 1).ConfigureAwait(true);
                }

                var retained = rows.Children.ToArray();
                state = Fold(state, """{"kind":"tool_progress","tool":{"id":"review-layout","name":"reasonix.subagent.status","output":"running","profile":{"name":"review","model":"provider/model"}}}""");
                state = Fold(state, """{"kind":"tool_result","tool":{"id":"review-layout","name":"task","output":"ready","profile":{"name":"review"}}}""");
                w.ShowState(state);
                Assert.Equal(2, SubagentActivity.Of(state).Count(entry => entry.Running));
                Assert.True(pulse.IsAnimating(phase));
                Assert.Equal(retained, rows.Children.ToArray());
                Assert.Equal("ready", state.Items.OfType<ToolItem>().First().Call.Output);

                w.Classes.Remove("motion");
                Assert.False(pulse.IsAnimating(phase));
                Assert.True(pulse.IsVisible);
                var cancellations = new List<string>();
                reads.Exchange = request =>
                {
                    var path = request.RequestUri!.AbsolutePath;
                    if (request.Method == HttpMethod.Post && path.StartsWith("/tool-calls/", StringComparison.Ordinal) && path.EndsWith("/cancel", StringComparison.Ordinal))
                    {
                        cancellations.Add(path);
                        return Task.FromResult<HttpResponseMessage?>(new HttpResponseMessage(HttpStatusCode.NoContent));
                    }

                    if (path == "/subagents")
                    {
                        return Task.FromResult<HttpResponseMessage?>(new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json"),
                        });
                    }

                    if (path == "/processes")
                    {
                        return Task.FromResult<HttpResponseMessage?>(new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("""[{"name":"review","kind":"subagent","state":"waiting","cause":"slots"}]""", System.Text.Encoding.UTF8, "application/json"),
                        });
                    }

                    return Task.FromResult<HttpResponseMessage?>(null);
                };

                Click(chip.FindControl<Button>("StopAll")!);
                await WaitForMotionFrame(() => cancellations.Count == 2).ConfigureAwait(true);
                Assert.Equal(new[] { "/tool-calls/review-layout/cancel", "/tool-calls/review-motion/cancel" }, cancellations);

                state = Fold(state, """{"kind":"tool_progress","tool":{"id":"review-layout","name":"reasonix.subagent.status","output":"completed"}}""");
                state = Fold(state, """{"kind":"tool_result","tool":{"id":"review-motion","name":"task","output":"done","profile":{"name":"review"}}}""");
                w.ShowState(state);
                Assert.False(pulse.IsVisible);
                Assert.Equal(retained, rows.Children.ToArray());
                Assert.False(chip.FindControl<Button>("StopAll")!.IsVisible);
                deck.Close(true);
                Click(processes.FindControl<Button>("Trigger")!);
                await WaitForMotionFrame(() => processes.FindControl<StackPanel>("Rows")!.Children.OfType<TextBlock>().Any(text => text.Text == "等并发名额")).ConfigureAwait(true);
                processes.FindControl<StudioHoverCard>("Details")!.Close(true);
                Click(trigger);
                Click(rows.GetVisualDescendants().OfType<Button>().First(button => AutomationProperties.GetAutomationId(button) == "action:agent.open"));
                await WaitForMotionFrame(() => root.Children.OfType<Popup>().Any(popup => popup.IsOpen)).ConfigureAwait(true);

                double Progress() => (double)pulse.GetValue(phase)!;

                SessionState Fold(SessionState previous, string json)
                {
                    Assert.True(WireFrame.TryParse(json, out var frame));
                    return reducer.Reduce(previous, frame);
                }

                static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            },
            allowMotion: true);
}
