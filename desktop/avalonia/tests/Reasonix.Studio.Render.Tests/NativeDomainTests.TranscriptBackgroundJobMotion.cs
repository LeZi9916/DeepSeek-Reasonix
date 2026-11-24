using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Events;
using Reasonix.Studio.Contracts.Jobs;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.State;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Follows the tool result into the live job reading and opens the shared output tab.</summary>
    /// <param name="dark">Whether to use the dark palette.</param>
    /// <returns>The transcript-to-background-job interaction.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TranscriptBackgroundJobSharesStatusAndOutputTab(bool dark) =>
        WithSettingsAsync(
            "conversation",
            async (w, reads) =>
            {
                w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                await WaitForMotionFrame(() => w.Classes.Contains("motion")).ConfigureAwait(true);
                var started = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 739_000;
                var job = new BackgroundJobStatus
                {
                    Id = "job-0",
                    Description = "运行桌面端测试并只看最后 40 行输出",
                    Detail = "npm run tauri:test 2>&1 | Select-Object -Last 40",
                    Status = "running",
                    StartedAt = started,
                };
                var status = reads.Read<SessionStatus>("/status") with
                {
                    Jobs = [job],
                };
                reads.Exchange = request => Task.FromResult<HttpResponseMessage?>(
                    request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/jobs/job-0/output"
                        ? new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = JsonContent.Create(new BackgroundJobOutput
                            {
                                Status = "running",
                                Text = "watching for changes",
                            }),
                        }
                        : null);
                w.AdoptComposer(status);
                var reducer = new SessionReducer(new IdSequence());
                var state = Fold(new SessionState(), """{"kind":"turn_started","msgIndex":3,"authoredTurn":3,"text":"在后台运行界面测试"}""");
                state = Fold(state, """
                    {"kind":"tool_dispatch","tool":{"id":"background-check","name":"bash","args":"{\"command\":\"npm run tauri:test 2>&1 | Select-Object -Last 40\",\"background\":true}"}}
                    """);
                w.ShowState(state);
                w.UpdateLayout();
                var running = Header();
                Assert.True(running.FindControl<Canvas>("RunningStatus")!.IsVisible);
                Assert.False(running.FindControl<TextBlock>("JobNote")!.IsVisible);
                state = Fold(state, """
                    {"kind":"tool_result","tool":{"id":"background-check","name":"bash","args":"{\"command\":\"npm run tauri:test 2>&1 | Select-Object -Last 40\",\"background\":true}","output":"Background job started","execution":{"state":"background_started","jobId":"job-0"}}}
                    """);
                w.ShowState(state);
                w.UpdateLayout();
                var header = Header();
                var mark = header.FindControl<Viewbox>("StaticStatus")!;
                var note = header.FindControl<TextBlock>("JobNote")!;
                var reading = header.FindControl<TextBlock>("JobReading")!;
                var scheme = dark ? "dark" : "light";
                Assert.True(note.IsEffectivelyVisible);
                Assert.Equal(488, header.TranslatePoint(default, w)!.Value.X, 1);
                Assert.Equal(741, header.Bounds.Width, 1);
                Assert.Equal("放到后台运行", header.FindControl<TextBlock>("ToolName")!.Text);
                Assert.StartsWith("在跑 · 12 分", reading.Text, StringComparison.Ordinal);
                Assert.False(header.FindControl<Canvas>("RunningStatus")!.IsVisible);
                var toggle = header.FindControl<Button>("Toggle")!;
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STUDIO_MOTION_OUTPUT")))
                {
                    await WaitForMotionFrame(() => mark.RenderTransform!.Value.M11 is > 1.23 and < 1.45 && toggle.Opacity < 0.999).ConfigureAwait(true);
                    CaptureMotionRegion(w, header, -8, 8, 757, 61, $"transcript-background-handoff-{scheme}-75");
                }

                await WaitForMotionFrame(() => mark.RenderTransform!.Value.M11 == 1 && toggle.Opacity == 1).ConfigureAwait(true);
                CaptureMotionRegion(w, header, -8, 8, 757, 61, $"transcript-background-job-{scheme}-settled");

                var link = JobViewButton(note);
                Assert.Equal("job-0", link.Tag);
                Click(link);
                var host = w.FindControl<ContentControl>("BackgroundJobHost")!;
                var view = Assert.IsAssignableFrom<UserControl>(host.Content);
                await WaitForMotionFrame(() => view.FindControl<SelectableTextBlock>("Output")!.Text == "watching for changes").ConfigureAwait(true);
                var chip = w.FindControl<UserControl>("BackgroundJobs")!;
                var details = chip.FindControl<StudioHoverCard>("Details")!;
                details.Open();
                w.UpdateLayout();
                Click(JobViewButton(details.FindControl<Border>("Frame")!));
                Assert.Same(view, host.Content);
                Assert.Single(w.FindControl<StackPanel>("FileTabs")!.Children);

                var ended = status with
                {
                    Jobs = [],
                    EndedJobs =
                    [
                        job with
                        {
                            Status = "done",
                            EndedAt = started + 741_000,
                            ExitCode = 0,
                        },
                    ],
                };
                w.AdoptComposer(ended);
                Assert.Same(header, Header());
                Assert.False(note.IsVisible);
                Assert.Contains("退出 0", reading.Text, StringComparison.Ordinal);
                Assert.False(InlineLinkVisible(note));
                Assert.Equal(1, mark.RenderTransform!.Value.M11);

                w.AdoptComposer(ended with
                {
                    EndedJobs = [],
                });
                Assert.False(reading.IsVisible);
                Assert.False(note.IsVisible);

                SessionState Fold(SessionState previous, string json)
                {
                    Assert.True(WireFrame.TryParse(json, out var frame));
                    return reducer.Reduce(previous, frame);
                }

                UserControl Header() => w.GetVisualDescendants().OfType<UserControl>().Single(control => control.GetType().Name == "ToolCallHeader");
                static Button JobViewButton(Control within) => within.GetVisualDescendants().OfType<Button>()
                    .First(button => AutomationProperties.GetAutomationId(button) == "action:job.view");
                static bool InlineLinkVisible(Control within) => within.GetVisualDescendants().OfType<Button>().Any(button => button.IsEffectivelyVisible);
                static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            },
            allowMotion: true);
}
