using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Shell.Composer;
using Reasonix.Studio.Shell.Host;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Captures the pending line while the reference work-mode operation is in flight.</summary>
    /// <param name="dark">Whether the visual fixture uses the dark palette.</param>
    /// <returns>The native input feedback check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ComposerPendingSweepFollowsModeRequest(bool dark) => WithSettingsAsync(
        "conversation",
        async (window, reads) =>
        {
            window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            await WaitForMotionFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
            var status = reads.Read<JsonObject>("/status");
            status["plan"] = false;
            var response = new TaskCompletionSource<HttpResponseMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var writes = 0;
            reads.Exchange = request =>
            {
                if (request.RequestUri!.AbsolutePath == "/plan" && request.Method == HttpMethod.Post)
                {
                    writes++;
                    return response.Task;
                }

                return Task.FromResult(request.RequestUri.AbsolutePath == "/status" ? JsonReply(status) : null);
            };

            window.AdoptComposer(reads.Read<SessionStatus>("/status"));
            var trigger = window.FindControl<Button>("PlanMode")!;
            var popup = window.FindControl<ComposerPopup>("ModePopup")!;
            var pending = window.FindControl<ComposerPendingSweep>("PendingComposerSweep")!;
            var footer = window.FindControl<Grid>("ComposeFooter")!;
            Assert.False(pending.IsVisible);
            trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(popup.IsOpen);
            var plan = window.FindControl<ModePanel>("ModePanel")!.FindControl<Button>("PlanOption")!;
            plan.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(pending.IsVisible);
            Assert.Contains("pending", trigger.Classes);
            Assert.True(trigger.IsEnabled);
            Assert.False(popup.IsOpen);
            Assert.Equal(1, writes);
            trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(popup.IsOpen);
            Assert.Equal(1, writes);

            var scheme = dark ? "dark" : "light";
            window.UpdateLayout();
            var line = pending.TranslatePoint(default, window)!.Value;
            Assert.InRange(line.Y, 870, 872);
            Assert.InRange(pending.Bounds.Width, 759, 761);
            foreach (var target in new[] { 230, 620 })
            {
                await WaitForMotionFrame(() => pending.Phase >= target / 1100d).ConfigureAwait(true);
                CaptureMotionRegion(window, footer, -13, 18, 770, 76, $"compose-pending-{scheme}-{target}");
            }

            status["plan"] = true;
            response.SetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            await WaitForMotionFrame(() => !pending.IsVisible && trigger.Opacity >= 0.99).ConfigureAwait(true);
            Assert.Equal("Plan", window.FindControl<TextBlock>("PlanModeLabel")!.Text);
            Assert.DoesNotContain("pending", trigger.Classes);
            Assert.Equal(1, writes);
            CaptureMotionRegion(window, footer, -13, 18, 770, 76, $"compose-pending-{scheme}-settled");
        },
        workspacePreferences: new WorkspacePreferences { ShowModePicker = true },
        allowMotion: true);

    /// <summary>Keeps the tool-row feedback while an independent setting is still settling.</summary>
    /// <returns>The concurrent setting interaction.</returns>
    [Fact]
    public Task ComposerPendingSweepWaitsForIndependentEdits() => WithSettingsAsync(
        "conversation",
        async (window, reads) =>
        {
            var status = reads.Read<JsonObject>("/status");
            status["plan"] = false;
            var mode = new TaskCompletionSource<HttpResponseMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var effort = new TaskCompletionSource<HttpResponseMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var catalog = new TaskCompletionSource<HttpResponseMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var holdCatalog = false;
            var catalogStarted = false;
            reads.Exchange = request => request.RequestUri!.AbsolutePath switch
            {
                "/plan" when request.Method == HttpMethod.Post => mode.Task,
                "/effort" when request.Method == HttpMethod.Post => effort.Task,
                "/models" when holdCatalog => WaitForCatalog(),
                "/status" => Task.FromResult<HttpResponseMessage?>(JsonReply(status)),
                _ => Task.FromResult<HttpResponseMessage?>(null),
            };
            window.AdoptComposer(reads.Read<SessionStatus>("/status"));
            var planTrigger = window.FindControl<Button>("PlanMode")!;
            var modelTrigger = window.FindControl<Button>("ModelButton")!;
            var pending = window.FindControl<ComposerPendingSweep>("PendingComposerSweep")!;
            planTrigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.FindControl<ModePanel>("ModePanel")!.FindControl<Button>("PlanOption")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("pending", planTrigger.Classes);
            Assert.DoesNotContain("pending", modelTrigger.Classes);

            modelTrigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var picker = window.FindControl<ModelPicker>("ModelPicker")!;
            await WaitForMotionFrame(() => picker.IsOpen).ConfigureAwait(true);
            var high = picker.FindControl<StackPanel>("EffortList")!.Children.OfType<Button>().Single(button => Equals(button.Tag, "high"));
            high.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("pending", modelTrigger.Classes);
            Assert.True(pending.IsVisible);

            status["plan"] = true;
            mode.SetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            await WaitForMotionFrame(() => !planTrigger.Classes.Contains("pending")).ConfigureAwait(true);
            Assert.Contains("pending", modelTrigger.Classes);
            Assert.True(pending.IsVisible);

            status["effort"] = "high";
            holdCatalog = true;
            try
            {
                effort.SetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
                await WaitForMotionFrame(() => catalogStarted && !pending.IsVisible).ConfigureAwait(true);
                Assert.DoesNotContain("pending", modelTrigger.Classes);
            }
            finally
            {
                catalog.TrySetResult(JsonReply(reads.Read<JsonObject>("/models")));
            }

            Task<HttpResponseMessage?> WaitForCatalog()
            {
                catalogStarted = true;
                return catalog.Task;
            }
        },
        workspacePreferences: new WorkspacePreferences { ShowModePicker = true });

    /// <summary>Checks that the reduced-motion line remains still and clears on refusal.</summary>
    /// <returns>The reduced-motion and refusal check.</returns>
    [Fact]
    public Task ComposerPendingSweepClearsOnRefusalWithReducedMotion() => WithSettingsAsync("conversation", async (window, reads) =>
    {
        var response = new TaskCompletionSource<HttpResponseMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        reads.Exchange = request => request.RequestUri!.AbsolutePath == "/plan" ? response.Task : Task.FromResult<HttpResponseMessage?>(null);
        var trigger = window.FindControl<Button>("PlanMode")!;
        var pending = window.FindControl<ComposerPendingSweep>("PendingComposerSweep")!;
        trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.FindControl<ModePanel>("ModePanel")!.FindControl<Button>("PlanOption")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(pending.IsVisible);
        Assert.Equal(-1, pending.Phase);
        response.SetResult(new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("refused") });
        await WaitForMotionFrame(() => !pending.IsVisible).ConfigureAwait(true);
        Assert.DoesNotContain("pending", trigger.Classes);
    });
}
