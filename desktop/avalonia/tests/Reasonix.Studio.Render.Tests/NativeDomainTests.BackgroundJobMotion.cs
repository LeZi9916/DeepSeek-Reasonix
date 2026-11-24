using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Kernel.Client;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Captures the running-job rail marker from actual session status data.</summary>
    /// <param name="dark">Whether to use the dark palette.</param>
    /// <returns>The appearance and lifecycle check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task BackgroundJobsFollowStatusAndMotionPreference(bool dark) =>
        WithSettingsAsync(
            "conversation",
            async (w, _) =>
            {
                w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                await WaitForMotionFrame(() => w.Classes.Contains("motion")).ConfigureAwait(true);
                var status = JsonSerializer.Deserialize<SessionStatus>("""
                    {"sessionPath":"/sessions/jobs.jsonl","jobs":[
                        {"id":"job-0","label":"监视界面","description":"监视界面","status":"running"},
                        {"id":"job-1","label":"内核测试","description":"内核测试","status":"running"},
                        {"id":"job-2","label":"工作台构建","description":"工作台构建","status":"running"}
                    ]}
                    """)!;
                var chip = w.FindControl<UserControl>("BackgroundJobs")!;
                var anchor = chip.FindControl<Button>("Trigger")!;
                var pulse = chip.FindControl<Control>("Pulse")!;
                var phase = AvaloniaPropertyRegistry.Instance.FindRegistered(pulse, "Phase")!;
                var details = chip.FindControl<StudioHoverCard>("Details")!;
                var scheme = dark ? "dark" : "light";
                Assert.False(chip.IsVisible);
                w.AdoptComposer(status);
                w.UpdateLayout();
                Assert.True(chip.IsVisible && pulse.IsEffectivelyVisible);
                Assert.Equal(22, anchor.Bounds.Height);
                Assert.Equal(6, pulse.Bounds.Width);

                var output = Environment.GetEnvironmentVariable("STUDIO_MOTION_OUTPUT");
                if (!string.IsNullOrEmpty(output))
                {
                    foreach (var (target, min, max) in new[] { (0, 0d, 0.06), (340, 0.40, 0.50), (800, 0.85, 0.92), (1120, 1d, 1d) })
                    {
                        await WaitForMotionFrame(() => pulse.IsAnimating(phase) && Progress() >= min && Progress() <= max).ConfigureAwait(true);
                        var stem = $"background-job-pulse-{scheme}-{target}";
                        CaptureMotionRegion(w, anchor, -8, 8, 159, 36, stem);
                        System.IO.File.WriteAllText(Path.Combine(output, $"native-{stem}-phase.txt"), FormattableString.Invariant($"{Progress():F6}"));
                    }
                }
                else
                {
                    await WaitForMotionFrame(() => pulse.IsAnimating(phase) && Progress() > 0 && Progress() < 1).ConfigureAwait(true);
                }

                w.Classes.Remove("motion");
                Assert.False(pulse.IsAnimating(phase));
                Assert.Equal(1, Progress());
                Assert.True(pulse.IsEffectivelyVisible);
                anchor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(details.IsOpen);
                Assert.Contains(
                    chip.FindControl<StackPanel>("Rows")!.Children.OfType<Border>(),
                    row => row.Child is StackPanel content && content.Children.Count > 0);

                w.AdoptComposer(status with
                {
                    Jobs = null,
                    EndedJobs =
                    [
                        status.Jobs![0] with
                        {
                            Status = "failed",
                            ExitCode = 2,
                        },
                    ],
                });
                Assert.False(details.IsOpen);
                Assert.Contains("failed", chip.Classes);
                Assert.False(pulse.IsVisible);
                anchor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(details.IsOpen);
                Assert.DoesNotContain("failed", chip.Classes);
                w.AdoptComposer(new SessionStatus { SessionPath = "/sessions/empty.jsonl" });
                Assert.False(chip.IsVisible);
                Assert.False(pulse.IsVisible);
                Assert.False(details.IsOpen);

                double Progress() => (double)pulse.GetValue(phase)!;
            },
            allowMotion: true);
}
