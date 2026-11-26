using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.State;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Verifies the plan's retained exit layer and focus return against the Electron close.</summary>
    /// <param name="dark">Whether to use the dark palette.</param>
    /// <returns>The close motion check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PlanCloseClipsAndFadesBeforeRestoringFocus(bool dark) =>
        WithSettingsAsync(
            "conversation",
            async (w, reads) =>
            {
                w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                w.AdoptComposer(reads.Read<SessionStatus>("/status"));
                w.Classes.Add("motion");
                w.ShowState(new SessionState
                {
                    Running = true,
                    RunSince = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Plan = new[]
                    {
                        new PlanStep { Text = "整理入口和约束", Status = TodoStatus.Completed },
                        new PlanStep { Text = "接入目前的进度", Status = TodoStatus.InProgress },
                        new PlanStep { Text = "核对截图和状态", Status = TodoStatus.Pending },
                    },
                });
                w.UpdateLayout();
                var scheme = dark ? "dark" : "light";
                var plan = w.FindControl<UserControl>("ComposerPlan")!;
                var root = w.FindControl<Grid>("LayoutRoot")!;
                var body = plan.FindControl<Border>("Body")!;
                var summary = plan.FindControl<Button>("Summary")!;
                var close = plan.FindControl<Button>("Close")!;
                var focusRing = plan.FindControl<Border>("SummaryFocusRing")!;

                async Task StartClose()
                {
                    summary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await WaitForMotionFrame(() => body.IsEffectivelyVisible && body.Opacity == 1 && !body.IsAnimating(Visual.OpacityProperty)).ConfigureAwait(true);
                    w.UpdateLayout();
                    Assert.True(body.Bounds.Height > 34);
                    Assert.True(close.Focus());
                    close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.True(body.IsVisible);
                    Assert.False(body.IsHitTestVisible);
                    Assert.False(summary.IsVisible);
                    Assert.False(summary.IsFocused);
                }

                var output = Environment.GetEnvironmentVariable("STUDIO_MOTION_OUTPUT");
                if (!string.IsNullOrEmpty(output))
                {
                    // An overloaded render loop can skip a short sampling window.
                    foreach (var (phase, low, high) in new[] { (35, 0.90, 0.986), (90, 0.43, 0.745) })
                    {
                        var captured = false;
                        for (var attempt = 0; attempt < 3 && !captured; attempt++)
                        {
                            await StartClose().ConfigureAwait(true);
                            await WaitForMotionFrame(() => !body.IsVisible || (body.Opacity > low && body.Opacity < high)).ConfigureAwait(true);
                            if (body.IsVisible)
                            {
                                w.UpdateLayout();
                                var stem = $"plan-close-{scheme}-{phase}";
                                CaptureMotionRegion(w, root, 483, -588, 768, 200, stem);
                                File.WriteAllText(Path.Combine(output, $"native-{stem}-pose.txt"), FormattableString.Invariant($"{body.Height:F3},{body.Opacity:F6}"));
                                captured = true;
                            }

                            await WaitForMotionFrame(() => summary.IsVisible && summary.IsFocused).ConfigureAwait(true);
                        }

                        Assert.True(captured, $"The renderer skipped the {phase} ms plan close sample.");
                    }
                }
                else
                {
                    await StartClose().ConfigureAwait(true);
                    await WaitForMotionFrame(() => summary.IsVisible && summary.IsFocused).ConfigureAwait(true);
                }

                Assert.False(body.IsVisible);
                Assert.True(focusRing.IsEffectivelyVisible);
                Assert.False(body.IsAnimating(Visual.OpacityProperty));
                Assert.True(double.IsNaN(body.Height));
                w.UpdateLayout();
                CaptureMotionRegion(w, root, 483, -588, 768, 200, $"plan-close-{scheme}-final");

                summary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(body.Opacity < 1 && double.IsFinite(body.Height));
                var currentHeight = body.Height;
                var currentOpacity = body.Opacity;
                var reversed = new List<(double Height, double Opacity)>();
                void CaptureReverse(object? sender, AvaloniaPropertyChangedEventArgs change)
                {
                    if (body.IsVisible && (change.Property == Visual.OpacityProperty || change.Property == Avalonia.Layout.Layoutable.HeightProperty))
                    {
                        reversed.Add((body.Height, body.Opacity));
                    }
                }

                body.PropertyChanged += CaptureReverse;
                close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitForMotionFrame(() => !body.IsVisible).ConfigureAwait(true);
                body.PropertyChanged -= CaptureReverse;
                Assert.NotEmpty(reversed);
                Assert.All(reversed, frame =>
                {
                    Assert.InRange(frame.Height, 0, currentHeight + 0.01);
                    Assert.InRange(frame.Opacity, 0, currentOpacity + 0.001);
                });

                await StartClose().ConfigureAwait(true);
                w.Classes.Remove("motion");
                Assert.False(body.IsVisible);
                Assert.True(summary.IsVisible && summary.IsFocused);

                // Leave with an unfinished close to exercise visual-tree teardown.
                w.Classes.Add("motion");
                await StartClose().ConfigureAwait(true);
            },
            allowMotion: true);
}
