using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Events;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Follows the provider wait through retries and the first answering frame.</summary>
    /// <param name="dark">Whether to use the dark palette.</param>
    /// <returns>The wait motion and state check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ModelWaitDotsFollowTheSameAttemptAcrossRetries(bool dark) =>
        WithSettingsAsync(
            "conversation",
            async (w, _) =>
            {
                w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                await WaitForMotionFrame(() => w.Classes.Contains("motion")).ConfigureAwait(true);
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var reducer = new SessionReducer(new IdSequence(), () => now);
                var state = new SessionState
                {
                    Items = new TranscriptItem[]
                    {
                        new UserItem { Id = "q", MsgIndex = 3, Text = "检查模型回包时的提示" },
                    },
                };

                state = Fold(state, """{"kind":"turn_started","msgIndex":3}""");
                w.ShowState(state);
                w.UpdateLayout();
                var wait = w.GetVisualDescendants().OfType<UserControl>().Single(control => control.GetType().Name == "ModelResponseWaitView");
                var first = wait.FindControl<Ellipse>("First")!;
                var second = wait.FindControl<Ellipse>("Second")!;
                var third = wait.FindControl<Ellipse>("Third")!;
                var label = wait.FindControl<TextBlock>("Label")!;
                var scheme = dark ? "dark" : "light";
                var capture = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STUDIO_MOTION_OUTPUT"));
                Assert.True(wait.IsEffectivelyVisible);
                Assert.Equal(25, wait.Bounds.Height);
                Assert.Equal(4, first.Bounds.Width);
                Assert.StartsWith("等待回包", label.Text, StringComparison.Ordinal);
                if (capture)
                {
                    await WaitForMotionFrame(() => first.Opacity is >= 0.22 and < 0.4 && second.Opacity == 1 && third.Opacity == 1).ConfigureAwait(true);
                    w.UpdateLayout();
                    CaptureMotionRegion(w, wait, -6, 6, 420, 37, $"model-wait-initial-{scheme}-0");
                    await WaitForMotionFrame(Peak).ConfigureAwait(true);
                    w.UpdateLayout();
                    CaptureMotionRegion(w, wait, -6, 6, 420, 37, $"model-wait-initial-{scheme}-575");
                }

                now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                state = Fold(state, """{"kind":"retrying","retryAttempt":2,"retryMax":5,"retryScope":"headers","retryCause":"upstream_status","retryStatus":503,"retryDelayMs":3000,"retryTimeoutSecs":60}""");
                var previousPhase = first.RenderTransform!.Value.M32;
                w.ShowState(state);
                Assert.Same(first, wait.FindControl<Ellipse>("First"));
                Assert.Equal(previousPhase, first.RenderTransform!.Value.M32);
                Assert.Contains("服务器返回 HTTP 503 ·", label.Text, StringComparison.Ordinal);
                Assert.Contains("3s 后重试 2/5", label.Text, StringComparison.Ordinal);
                if (capture)
                {
                    await WaitForMotionFrame(Peak).ConfigureAwait(true);
                    w.UpdateLayout();
                    CaptureMotionRegion(w, wait, -6, 6, 420, 37, $"model-wait-retry-{scheme}-575");
                }

                now -= 4000;
                state = Fold(state, """{"kind":"retrying","retryAttempt":3,"retryMax":0,"retryScope":"stream","retryCause":"stream_idle","retryDelayMs":1000}""");
                w.ShowState(state);
                Assert.Contains("回包停住了 · 重放 3/∞", label.Text, StringComparison.Ordinal);
                w.Classes.Remove("motion");
                Assert.True(wait.IsEffectivelyVisible);
                Assert.Equal(1, first.Opacity);
                Assert.Equal(0, first.RenderTransform!.Value.M32);
                state = Fold(state, """{"kind":"tool_dispatch","tool":{"id":"one","name":"read_file","args":"{}"}}""");
                w.ShowState(state);
                Assert.False(wait.IsVisible);

                now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                state = Fold(state, """{"kind":"tool_result","tool":{"id":"one","name":"read_file","output":"ready"}}""");
                w.ShowState(state);
                Assert.True(wait.IsVisible);
                ViewKeepsWaitingAfterNotice();
                state = Fold(state, """{"kind":"text","text":"已经收到回复。"}""");
                w.ShowState(state);
                Assert.False(wait.IsVisible);
                state = Fold(state, """{"kind":"tool_result","tool":{"id":"child","parentId":"one","name":"read_file","output":"done"}}""");
                w.ShowState(state);
                Assert.False(wait.IsVisible);

                void ViewKeepsWaitingAfterNotice()
                {
                    state = Fold(state, """{"kind":"notice","code":"fixture_notice","text":"收到状态更新。"}""");
                    w.ShowState(state);
                    Assert.True(wait.IsVisible);
                    Assert.Same(first, wait.FindControl<Ellipse>("First"));
                }

                bool Peak() => first.RenderTransform!.Value.M32 < -2.94
                    && second.RenderTransform!.Value.M32 is < -2.1 and > -2.85
                    && third.RenderTransform!.Value.M32 is < -0.9 and > -1.9;

                SessionState Fold(SessionState previous, string json)
                {
                    Assert.True(WireFrame.TryParse(json, out var frame));
                    return reducer.Reduce(previous, frame);
                }
            },
            allowMotion: true);
}
