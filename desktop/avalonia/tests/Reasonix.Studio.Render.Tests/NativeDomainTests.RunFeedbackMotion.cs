using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Wire;
using Reasonix.Studio.Shell.Composer;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks that the composer's readout follows the run and its billed rounds.</summary>
    /// <param name="dark">Whether the conversation uses the dark theme.</param>
    /// <returns>The runtime and usage motion check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RunFeedbackTracksUsageClockAndWaiting(bool dark) => WithSettingsAsync(
        "conversation",
        async (window, _) =>
        {
            window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            await WaitForMotionFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
            var since = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var state = new SessionState { Running = true, RunSince = since };
            var badge = window.FindControl<Border>("ComposerRunBadge")!;
            var clock = window.FindControl<TextBlock>("ComposerRunClock")!;
            var readings = window.FindControl<RunTokens>("ComposerRunTokens")!;
            var header = window.FindControl<Grid>("ComposeRunState")!;
            var changed = window.FindControl<Button>("ChangedFiles")!;
            changed.IsVisible = false;
            window.ShowState(state);
            window.UpdateLayout();
            Assert.True(badge.IsVisible);
            Assert.True(clock.IsVisible);
            Assert.False(readings.IsVisible);

            var stage = dark ? "dark" : "light";
            var stroke = badge.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().First();
            var before = clock.Text;

            state = state with
            {
                Metrics = new Metrics { Hit = 10, Miss = 190, Out = 12 },
            };
            window.ShowState(state);
            window.UpdateLayout();
            var sent = readings.FindControl<StackPanel>("Sent")!;
            var received = readings.FindControl<StackPanel>("Received")!;
            var sentValue = readings.FindControl<TextBlock>("SentValue")!;
            var receivedValue = readings.FindControl<TextBlock>("ReceivedValue")!;
            Assert.True(readings.IsEffectivelyVisible);
            Assert.Equal("200", sentValue.Text);
            Assert.Equal("12", receivedValue.Text);
            await WaitForMotionFrame(() => stroke.StrokeDashOffset <= -0.95).ConfigureAwait(true);
            CaptureMotionRegion(window, header, -16, 11, 768, 40, $"run-mark-{stage}-running");
            await WaitForMotionFrame(() => !sent.Classes.Contains("landed") && !received.Classes.Contains("landed")).ConfigureAwait(true);

            state = state with
            {
                Metrics = new Metrics { Hit = 20, Miss = 320, Out = 37 },
            };
            window.ShowState(state);
            var arrow = sent.GetVisualDescendants().OfType<Viewbox>().Single();
            Assert.Contains("landed", sent.Classes);
            Assert.Contains("landed", received.Classes);
            await WaitForMotionFrame(() => arrow.Opacity is > 0.65 and < 0.99).ConfigureAwait(true);
            var tint = Assert.IsAssignableFrom<ISolidColorBrush>(sentValue.Foreground);
            Assert.InRange(tint.Color.R, dark ? 128 : 0, dark ? 255 : 127);
            CaptureMotionRegion(window, header, -16, 11, 768, 40, $"run-feedback-{stage}-80");
            Assert.Equal("340", sentValue.Text);
            Assert.Equal("37", receivedValue.Text);
            await WaitForMotionFrame(() => !sent.Classes.Contains("landed") && !received.Classes.Contains("landed")).ConfigureAwait(true);
            CaptureMotionRegion(window, header, -16, 11, 768, 40, $"run-feedback-{stage}-final");
            await WaitForMotionFrame(() => clock.Text != before).ConfigureAwait(true);

            state = state with
            {
                OutLive = 8,
            };
            window.ShowState(state);
            Assert.Contains("estimated", received.Classes);
            Assert.Contains("landed", received.Classes);
            Assert.DoesNotContain("landed", sent.Classes);
            Assert.Equal("45", receivedValue.Text);

            var approval = new ApprovalItem { Id = "decision", Approval = new Approval { ID = "decision", Tool = "write" } };
            window.ShowState(state with
            {
                Items = new[] { approval },
            });
            Assert.Contains("waiting", badge.Classes);
            Assert.DoesNotContain("moving", badge.Classes);
            await WaitForMotionFrame(() => stroke.Opacity is > 0.5 and < 0.83).ConfigureAwait(true);
            CaptureMotionRegion(window, header, -16, 11, 768, 40, $"run-mark-{stage}-waiting");
            changed.IsVisible = true;
            window.ShowState(state with
            {
                Running = false,
                RunSince = null,
            });
            Assert.False(clock.IsVisible);
            Assert.False(readings.IsVisible);
            Assert.True(header.IsVisible);
        },
        allowMotion: true);

    /// <summary>Shares the status slot with the existing kernel file-change preference.</summary>
    /// <returns>The setting and composer interaction.</returns>
    [Fact]
    public Task RunFeedbackHonorsTheFileChangesPreference() => WithSettingsAsync("conversation", async (window, reads) =>
    {
        var show = true;
        var saved = 0;
        reads.Exchange = async request =>
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/changed-files-settings":
                    if (request.Method == HttpMethod.Post)
                    {
                        var payload = JsonNode.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false))!;
                        show = (bool)payload["show"]!;
                        saved++;
                    }

                    return JsonReply(new JsonObject { ["show"] = show });
                case "/git/repos":
                    return JsonReply(JsonNode.Parse("""{"repos":[{"dir":"","branch":"main","repo":true}]}""")!);
                case "/changes":
                    return JsonReply(JsonNode.Parse("""{"repo":true,"changes":[{"path":"README.md","status":"M","insertions":1}]}""")!);
                default:
                    return null;
            }
        };
        var changed = window.FindControl<Button>("ChangedFiles")!;
        var tokens = window.FindControl<RunTokens>("ComposerRunTokens")!;
        window.FindControl<Button>("GitRetry")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitForMotionFrame(() => changed.IsVisible).ConfigureAwait(true);
        window.ShowState(new SessionState
        {
            Running = true,
            Metrics = new Metrics { Hit = 20, Miss = 5, Out = 12 },
        });
        Assert.False(tokens.IsVisible);

        Navigate(window, "settings-display");
        var toggle = ControlAt<ToggleSwitch>(window, "action:chrome.changed-files");
        Assert.True(toggle.IsChecked);
        toggle.IsChecked = false;
        ClickAt(window, "action:chrome.changed-files");
        await WaitForMotionFrame(() => saved == 1 && !changed.IsVisible && tokens.IsVisible).ConfigureAwait(true);
        ClickAt(window, "action:settings.close");
        Assert.True(tokens.IsEffectivelyVisible);
    });
}
