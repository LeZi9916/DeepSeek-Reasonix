using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Jobs;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Kernel.Client;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Opens a reported job's output, checks tab motion, and exercises follow and pause.</summary>
    /// <param name="dark">Whether to use the dark palette.</param>
    /// <returns>The background job workbench interaction.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task BackgroundJobOutputOpensInWorkbenchAndFollowsWhenRequested(bool dark) =>
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
                        new BackgroundJobStatus
                        {
                            Id = "job-0",
                            Description = "运行桌面端测试并只看最后 40 行输出",
                            Detail = "npm run tauri:test 2>&1 | Select-Object -Last 40",
                            Status = "running",
                            StartedAt = start,
                        },
                    ],
                };
                var polls = 0;
                reads.Exchange = request =>
                {
                    if (request.Method != HttpMethod.Get || request.RequestUri!.AbsolutePath != "/jobs/job-0/output")
                    {
                        return Task.FromResult<HttpResponseMessage?>(null);
                    }

                    polls++;
                    var count = polls switch
                    {
                        1 => 28,
                        2 => 60,
                        _ => 64,
                    };
                    var lines = Enumerable.Range(0, count).Select(i => FormattableString.Invariant($"[{i:00}] compiling module {(i * 7) + 3} … ok"));
                    return Task.FromResult<HttpResponseMessage?>(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(new BackgroundJobOutput
                        {
                            Status = "running",
                            Text = string.Join('\n', lines) + "\nwatching for changes\n",
                        }),
                    });
                };

                w.AdoptComposer(status);
                w.UpdateLayout();
                var chip = w.FindControl<UserControl>("BackgroundJobs")!;
                var details = chip.FindControl<StudioHoverCard>("Details")!;
                var side = w.FindControl<Border>("WorkbenchPanel")!;
                var host = w.FindControl<ContentControl>("BackgroundJobHost")!;
                var tabs = w.FindControl<StackPanel>("FileTabs")!;
                var root = w.FindControl<Grid>("LayoutRoot")!;
                var scheme = dark ? "dark" : "light";
                var capture = Environment.GetEnvironmentVariable("STUDIO_MOTION_OUTPUT");

                OpenJob();
                Assert.False(details.IsOpen);
                Assert.True(side.IsVisible && host.IsVisible);
                var tab = Assert.Single(tabs.Children);
                if (!string.IsNullOrEmpty(capture))
                {
                    await WaitForMotionFrame(() => side.IsAnimating(Layoutable.WidthProperty) && side.Width is > 215 and < 394).ConfigureAwait(true);
                    w.UpdateLayout();
                    CaptureMotionRegion(w, root, 914, -47, 525, 200, $"background-job-dock-{scheme}-38");
                    File.WriteAllText(
                        Path.Combine(capture, $"native-background-job-dock-{scheme}-38-pose.txt"),
                        FormattableString.Invariant($"{side.Width:F3},{tab.Opacity:F6}"));
                }

                var view = Assert.IsAssignableFrom<UserControl>(host.Content);
                var scroll = view.FindControl<ScrollViewer>("OutputScroll")!;
                var output = view.FindControl<SelectableTextBlock>("Output")!;
                var follow = view.FindControl<Button>("Follow")!;
                Assert.Equal("$ npm run tauri:test 2>&1 | Select-Object -Last 40", view.FindControl<SelectableTextBlock>("Command")!.Text);
                await WaitForMotionFrame(() => output.Text?.Contains("[00] compiling module 3", StringComparison.Ordinal) == true).ConfigureAwait(true);
                await WaitForMotionFrame(() => !side.IsAnimating(Layoutable.WidthProperty) && !tab.IsAnimating(Layoutable.MaxWidthProperty)).ConfigureAwait(true);
                w.UpdateLayout();
                Assert.Equal(923, host.TranslatePoint(default, w)!.Value.X, 1);
                Assert.Equal(99, host.TranslatePoint(default, w)!.Value.Y, 1);
                Assert.Equal(508, host.Bounds.Width, 1);
                CaptureMotionRegion(w, root, 914, -47, 525, 200, $"background-job-dock-{scheme}-260");
                CaptureMotionRegion(w, root, 915, -47, 524, 260, $"background-job-output-{scheme}-settled");

                OpenJob();
                Assert.Same(view, host.Content);
                Assert.Single(tabs.Children);

                await WaitForMotionFrame(() => scroll.Extent.Height - scroll.Viewport.Height > 150 && AtBottom()).ConfigureAwait(true);
                Assert.False(follow.IsVisible);
                scroll.Offset = default;
                await WaitForMotionFrame(() => follow.IsVisible).ConfigureAwait(true);
                var deadline = DateTime.UtcNow.AddSeconds(6);
                while (output.Text?.Contains("[63] compiling module 444", StringComparison.Ordinal) != true && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(20).ConfigureAwait(true);
                }

                Assert.Contains("[63] compiling module 444", output.Text, StringComparison.Ordinal);
                Assert.Equal(0, scroll.Offset.Y);
                Click(follow);
                await WaitForMotionFrame(() => AtBottom() && !follow.IsVisible).ConfigureAwait(true);

                Click(w.FindControl<Button>("CloseWorkbench")!);
                await WaitForMotionFrame(() => !side.IsVisible).ConfigureAwait(true);
                var hiddenAt = polls;
                await Task.Delay(2100).ConfigureAwait(true);
                Assert.Equal(hiddenAt, polls);

                Click(w.FindControl<Button>("ViewSwitch")!);
                await WaitForMotionFrame(() => side.IsVisible && polls > hiddenAt).ConfigureAwait(true);
                w.UpdateLayout();
                Click(tab.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:workbench.close-job"));
                await WaitForMotionFrame(() => tabs.Children.Count == 0 && !host.IsVisible).ConfigureAwait(true);

                bool AtBottom() => scroll.Extent.Height - scroll.Viewport.Height - scroll.Offset.Y <= 24;

                void OpenJob()
                {
                    details.Open();
                    w.UpdateLayout();
                    var button = details.FindControl<Border>("Frame")!.GetVisualDescendants().OfType<Button>()
                        .Single(button => AutomationProperties.GetAutomationId(button) == "action:job.view");
                    Click(button);
                }

                static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            },
            allowMotion: true);
}
