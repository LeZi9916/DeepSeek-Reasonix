using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Linq;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Shows the timed preview for a question and jumps to that question from its mark.</summary>
    /// <param name="dark">Whether to use the dark palette.</param>
    /// <returns>The message rail interaction.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task MessageRailPreviewSlidesAndJumps(bool dark) =>
        WithSettingsAsync(
            "conversation",
            async (w, _) =>
            {
                w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                await WaitForMotionFrame(() => w.Classes.Contains("motion")).ConfigureAwait(true);
                var items = Enumerable.Range(1, 7)
                    .SelectMany(index => new TranscriptItem[]
                    {
                        new UserItem { Id = $"q{index}", MsgIndex = index * 2, Text = $"请核对第 {index} 处阶段变化与实际结果。" },
                        new SayItem { Id = $"a{index}", Done = true, Text = string.Join("\n\n", Enumerable.Repeat("核对截图与交互状态后记录差异。", 8)) },
                    })
                    .ToArray();
                w.ShowState(new SessionState { Items = items });
                w.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var conversation = w.GetVisualDescendants().OfType<SessionPage>().Single(page => page.IsEffectivelyVisible);
                var scroll = conversation.GetVisualDescendants().OfType<ScrollViewer>().First();
                var track = conversation.GetVisualDescendants().OfType<Canvas>().Single(canvas => canvas.Name == "Track");
                await WaitForMotionFrame(() => track.Children.OfType<Button>().Count() == 7).ConfigureAwait(true);
                scroll.Offset = default;
                var mark = track.Children.OfType<Button>().ElementAt(2);
                var peek = conversation.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "Peek");
                var y = Canvas.GetTop(mark) + (mark.Height / 2);
                var scheme = dark ? "dark" : "light";
                var origin = track.TranslatePoint(new Point(0, y), w)!.Value;
                var pointerAt = new Point(origin.X + 16, origin.Y);

                // Deliver the move before the headless helper pumps multiple render frames.
                using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
                track.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, track, pointer, w, pointerAt, 0, default, KeyModifiers.None));
                Assert.True(peek.IsVisible);
                Assert.Contains("shown", peek.Classes);
                var rail = Assert.IsAssignableFrom<Control>(track.GetVisualParent()!.GetVisualParent());
                var capture = Environment.GetEnvironmentVariable("STUDIO_MOTION_OUTPUT") is not null;
                if (capture)
                {
                    await WaitForMotionFrame(() => peek.Opacity is >= 0.89 and < 0.995).ConfigureAwait(true);
                    CaptureMotionRegion(w, rail, (int)rail.Bounds.Width - 310, 64 - (int)y, 310, 128, $"message-rail-{scheme}-45");
                }

                await WaitForMotionFrame(() => peek.Opacity == 1).ConfigureAwait(true);
                CaptureMotionRegion(w, rail, (int)rail.Bounds.Width - 310, 64 - (int)y, 310, 128, $"message-rail-{scheme}-final");
                Assert.Equal("你的第3条", peek.GetVisualDescendants().OfType<TextBlock>().Single(label => label.Name == "Heading").Text);
                w.MouseMove(pointerAt);
                w.MouseDown(pointerAt, MouseButton.Left);
                w.MouseUp(pointerAt, MouseButton.Left);
                await WaitForMotionFrame(() => scroll.Offset.Y > 100).ConfigureAwait(true);
                var landed = conversation.GetVisualDescendants().OfType<Border>().Where(b => b.Name == "Bubble").ElementAt(2);
                var outline = conversation.GetVisualDescendants().OfType<Control>().Single(control => control.GetType().Name == "MessageLandingHighlight");
                var flash = AvaloniaPropertyRegistry.Instance.FindRegistered(outline, "Amount")!;
                if (capture)
                {
                    await WaitForMotionFrame(() => (double)outline.GetValue(flash)! >= 0.95).ConfigureAwait(true);
                    CaptureMotionRegion(w, landed, -10, 10, 285, 72, $"message-jump-{scheme}-highlight");
                }

                await WaitForMotionFrame(() => !outline.IsAnimating(flash)).ConfigureAwait(true);
                CaptureMotionRegion(w, landed, -10, 10, 285, 72, $"message-jump-{scheme}-final");
                w.MouseMove(new Point(20, 20));
                Assert.False(peek.IsVisible);
            },
            allowMotion: true);
}
