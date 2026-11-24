using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Jobs;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Kernel.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Verifies the retained job popup, its anchored motion and the explicit stop confirmation.</summary>
    /// <param name="dark">Whether to use the dark palette.</param>
    /// <returns>The background job card motion check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task BackgroundJobDeckKeepsItsAnchorOnOpenAndClose(bool dark) =>
        WithSettingsAsync(
            "conversation",
            async (w, reads) =>
            {
                w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                await WaitForMotionFrame(() => w.Classes.Contains("motion")).ConfigureAwait(true);
                var start = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 739_000;
                var status = reads.Read<SessionStatus>("/status") with
                {
                    Jobs =
                    [
                        new BackgroundJobStatus { Id = "job-0", Description = "运行桌面端测试并只看最后 40 行输出", Detail = "npm run tauri:test 2>&1 | Select-Object -Last 40", Status = "running", StartedAt = start },
                        new BackgroundJobStatus { Id = "job-1", Description = "跑第 1 组内核测试", Detail = "go test ./internal/... -run Case1 -count=1 -timeout 10m", Status = "running", StartedAt = start },
                        new BackgroundJobStatus { Id = "job-2", Description = "跑第 2 组内核测试", Detail = "go test ./internal/... -run Case2 -count=1 -timeout 10m", Status = "running", StartedAt = start },
                    ],
                    EndedJobs =
                    [
                        new BackgroundJobStatus { Id = "job-finished", Label = "内核测试", Status = "done", StartedAt = start, EndedAt = start + 9000 },
                        new BackgroundJobStatus { Id = "job-failed", Label = "界面测试", Status = "failed", StartedAt = start, EndedAt = start + 16000, ExitCode = 1 },
                    ],
                };
                var stops = new List<string>();
                reads.Exchange = request =>
                {
                    var path = request.RequestUri!.AbsolutePath;
                    HttpResponseMessage? response = null;
                    if (request.Method == HttpMethod.Post && path.StartsWith("/jobs/", StringComparison.Ordinal) && path.EndsWith("/cancel", StringComparison.Ordinal))
                    {
                        stops.Add(path);
                        response = new HttpResponseMessage(HttpStatusCode.NoContent);
                    }
                    else if (path == "/status" && stops.Count == 3)
                    {
                        var killed = status.Jobs!.Select(job => job with
                        {
                            Status = "killed",
                            EndedAt = start + 739_000,
                        });
                        var settled = status with
                        {
                            Jobs = [],
                            EndedJobs = [.. status.EndedJobs!, .. killed],
                        };
                        response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(settled) };
                    }

                    return Task.FromResult(response);
                };
                w.AdoptComposer(status);
                w.UpdateLayout();
                var chip = w.FindControl<UserControl>("BackgroundJobs")!;
                var details = chip.FindControl<StudioHoverCard>("Details")!;
                var anchor = chip.FindControl<Button>("Trigger")!;
                var popup = details.FindControl<Popup>("Surface")!;
                var frame = details.FindControl<Border>("Frame")!;
                var root = w.FindControl<Grid>("LayoutRoot")!;
                var scheme = dark ? "dark" : "light";
                var capture = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STUDIO_MOTION_OUTPUT"));
                Assert.False(popup.IsOpen);

                Click(anchor);
                Assert.True(popup.IsOpen && details.IsOpen);
                if (capture)
                {
                    await Sample(false, 35, 0.64, 0.95).ConfigureAwait(true);
                    await WaitForMotionFrame(() => frame.Opacity == 1).ConfigureAwait(true);
                    details.Close(immediately: true);
                    Click(anchor);
                    await Sample(false, 90, 0.986, 0.99999).ConfigureAwait(true);
                }

                await WaitForMotionFrame(() => frame.Opacity == 1 && !frame.IsAnimating(Visual.OpacityProperty)).ConfigureAwait(true);
                w.UpdateLayout();
                Assert.Equal(360, frame.Bounds.Width);
                Assert.Equal(342, frame.Bounds.Height);
                Assert.Equal(521, frame.TranslatePoint(default, w)!.Value.Y, 1);
                Assert.Equal(anchor.TranslatePoint(default, w)!.Value.X + anchor.Bounds.Width, frame.TranslatePoint(default, w)!.Value.X + frame.Bounds.Width, 1);
                CaptureMotionRegion(w, root, 858, -511, 384, 389, $"background-job-deck-{scheme}-120");

                details.Close();
                Assert.True(popup.IsOpen);
                Assert.False(frame.IsHitTestVisible);
                if (capture)
                {
                    await Sample(true, 35, 0.05, 0.46).ConfigureAwait(true);
                    await WaitForMotionFrame(() => !popup.IsOpen).ConfigureAwait(true);
                    Click(anchor);
                    await WaitForMotionFrame(() => frame.Opacity == 1).ConfigureAwait(true);
                    details.Close();
                    await Sample(true, 90, 0.00001, 0.018).ConfigureAwait(true);
                }

                await WaitForMotionFrame(() => !popup.IsOpen).ConfigureAwait(true);
                CaptureMotionRegion(w, root, 858, -511, 384, 389, $"background-job-deck-close-{scheme}-120");

                Click(anchor);
                await WaitForMotionFrame(() => frame.Opacity == 1).ConfigureAwait(true);
                var arm = ByAction("action:job.arm-all");
                Click(arm);
                w.UpdateLayout();
                var confirm = ByAction("action:job.cancel-all");
                Assert.True(confirm.IsEffectivelyVisible);
                var confirmation = Assert.IsType<StackPanel>(confirm.GetVisualParent());
                await WaitForMotionFrame(() => confirmation.Opacity is > 0.50 and < 0.996).ConfigureAwait(true);
                Assert.Equal(14 * (1 - confirmation.Opacity), confirmation.RenderTransform!.Value.M31, 5);
                CaptureMotionRegion(w, root, 1094, -517, 145, 52, $"background-job-confirm-{scheme}-50");
                await WaitForMotionFrame(() => confirmation.Opacity == 1 && !confirmation.IsAnimating(Visual.OpacityProperty)).ConfigureAwait(true);
                Assert.Equal(0, confirmation.RenderTransform!.Value.M31);
                CaptureMotionRegion(w, root, 1094, -517, 145, 52, $"background-job-confirm-{scheme}-180");
                details.Close();
                await WaitForMotionFrame(() => frame.Opacity > 0 && frame.Opacity < 0.98).ConfigureAwait(true);
                var held = frame.Opacity;
                details.Open();
                Assert.Equal(held, frame.Opacity, 5);
                details.Close();
                await WaitForMotionFrame(() => !popup.IsOpen).ConfigureAwait(true);

                Click(anchor);
                w.Classes.Remove("motion");
                Assert.True(details.IsOpen);
                Assert.Equal(1, frame.Opacity);
                Assert.Equal(0, frame.RenderTransform!.Value.M32);
                details.Close();
                Assert.False(popup.IsOpen);

                Assert.Empty(stops);
                Click(anchor);
                w.UpdateLayout();
                Click(ByAction("action:job.arm-all"));
                Assert.Empty(stops);
                Click(ByAction("action:job.cancel-all"));
                await WaitForMotionFrame(() => !chip.FindControl<Control>("Pulse")!.IsVisible).ConfigureAwait(true);
                Assert.Equal(new[] { "/jobs/job-0/cancel", "/jobs/job-1/cancel", "/jobs/job-2/cancel" }, stops);
                w.UpdateLayout();
                Click(ByAction("action:job.clear"));
                Assert.False(chip.IsVisible);

                Button ByAction(string id) => frame.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == id);
                static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                async Task Sample(bool closing, int phase, double low, double high)
                {
                    await WaitForMotionFrame(() => popup.IsOpen && frame.Opacity > low && frame.Opacity < high).ConfigureAwait(true);
                    var opacity = frame.Opacity;
                    Assert.Equal(1 - (0.015 * (1 - opacity)), frame.RenderTransform!.Value.M11, 5);
                    Assert.Equal(4 * (1 - opacity), frame.RenderTransform!.Value.M32, 5);
                    w.UpdateLayout();
                    var stem = closing ? "background-job-deck-close" : "background-job-deck";
                    CaptureMotionRegion(w, root, 858, -511, 384, 389, $"{stem}-{scheme}-{phase}");
                }
            },
            allowMotion: true);
}
