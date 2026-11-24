using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
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
    /// <summary>Uses a real pointer selection to check the toolbar's placement, motion and commands.</summary>
    /// <param name="dark">Whether the transcript uses the dark theme.</param>
    /// <param name="below">Whether there is room above the selected text.</param>
    /// <returns>The native selection toolbar check.</returns>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task ReplySelectionSettlesAtTheSelectedText(bool dark, bool below) => WithSettingsAsync(
        "conversation",
        async (window, _) =>
        {
            window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            await WaitForMotionFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
            var passage = below ? "第 8 段回答，说明刚才那一步做了什么。" : "第一处是顶部间距，第二处是输入框边框。";
            var items = Enumerable.Range(0, 22).SelectMany(index => new TranscriptItem[]
            {
                new UserItem { Id = $"q{index}", Text = $"请记录第 {index + 1} 处差异。" },
                new SayItem { Id = $"a{index}", Done = true, Text = passage },
            }).ToArray();
            window.FindControl<Button>("ChangedFiles")!.IsVisible = true;
            window.ShowState(new SessionState { Items = items });
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var page = window.GetVisualDescendants().OfType<SessionPage>().Single(control => control.IsEffectivelyVisible);
            var overlay = page.GetVisualDescendants().OfType<Canvas>().Single(canvas => canvas.Name == "ReplySelectionLayer");
            var toolbar = overlay.FindControl<Border>("Toolbar")!;
            var scroll = page.GetVisualDescendants().OfType<ScrollViewer>().First();
            var reply = page.GetVisualDescendants().OfType<SelectableTextBlock>().Where(text => text.Name == "Answer").ElementAt(8);
            var rect = reply.TextLayout.HitTestTextRange(0, 14).First();
            var at = reply.TranslatePoint(rect.TopLeft, scroll)!.Value;
            scroll.Offset = new Vector(0, scroll.Offset.Y + at.Y - (below ? 24 : 230));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var drag = reply.TranslatePoint(new Point(rect.X + 1, rect.Y + (rect.Height / 2)), window)!.Value;
            var end = new Point(drag.X + rect.Width - 2, drag.Y);
            Assert.False(toolbar.IsVisible);
            window.MouseMove(drag);
            window.MouseDown(drag, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left);
            var selected = reply.SelectedText;
            Assert.False(string.IsNullOrWhiteSpace(selected));

            var capture = Environment.GetEnvironmentVariable("STUDIO_MOTION_OUTPUT") is not null;
            await WaitForMotionFrame(() => toolbar.IsVisible && (!capture || toolbar.Opacity is > 0.35 and < 0.99)).ConfigureAwait(true);
            Assert.Contains(below ? "below" : "above", toolbar.Classes);
            if (capture)
            {
                Assert.Equal(below, toolbar.RenderTransform!.Value.M32 < 0);
            }

            var text = reply.TextLayout.HitTestTextRange(Math.Min(reply.SelectionStart, reply.SelectionEnd), Math.Abs(reply.SelectionEnd - reply.SelectionStart)).First();
            var selectionAt = reply.TranslatePoint(text.TopLeft, overlay)!.Value;
            Assert.InRange(Math.Abs(Canvas.GetLeft(toolbar) - selectionAt.X), 0, 1);
            var expectedTop = below ? selectionAt.Y + text.Height + 6 : selectionAt.Y - 38;
            Assert.InRange(Math.Abs(Canvas.GetTop(toolbar) - expectedTop), 0, 1);
            var absolute = reply.TranslatePoint(text.TopLeft, window)!.Value;
            var overlayAt = overlay.TranslatePoint(default, window)!.Value;
            var dx = (int)Math.Floor(absolute.X - overlayAt.X) - 16;
            var top = 49 - (int)Math.Floor(absolute.Y - overlayAt.Y);
            var name = $"selection-tools-{(below ? "below" : "above")}-{(dark ? "dark" : "light")}";
            CaptureMotionRegion(window, overlay, dx, top, 330, 125, $"{name}-35");

            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            await WaitForMotionFrame(() => toolbar.Opacity == 1 && toolbar.Bounds.Width > 0).ConfigureAwait(true);
            CaptureMotionRegion(window, overlay, dx, top, 330, 125, $"{name}-settled");
            var action = overlay.FindControl<Button>(below ? "CopyButton" : "QuoteButton")!;
            var click = action.TranslatePoint(new Point(13, 12), window)!.Value;
            window.MouseDown(click, MouseButton.Left);
            Assert.Equal(selected, reply.SelectedText);
            var captureCopy = below && capture;
            if (captureCopy)
            {
                // The headless mouse release pumps frames; route the same button action before taking the entrance frame.
                action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            else
            {
                window.MouseUp(click, MouseButton.Left);
            }

            Assert.False(toolbar.IsVisible);

            if (below)
            {
                var notices = window.FindControl<StackPanel>("CopyNotices")!;
                await WaitForMotionFrame(() => notices.Children.Count == 1).ConfigureAwait(true);
                var note = Assert.IsType<Reasonix.Studio.Shell.Notices.CopyNotice>(notices.Children.Single());
                Assert.Equal("已复制", note.Caption);
                Assert.Equal(selected, reply.SelectedText);
                Assert.True(reply.IsFocused);

                var theme = dark ? "dark" : "light";
                var root = window.FindControl<Canvas>("CopyNoticeLayer")!;
                if (captureCopy)
                {
                    await WaitForMotionFrame(() => note.Opacity is > 0.75 and < 0.995).ConfigureAwait(true);
                    CaptureMotionRegion(window, root, 840, -674, 404, 70, $"selection-copy-note-{theme}-45");
                    window.MouseUp(click, MouseButton.Left);
                }

                await WaitForMotionFrame(() => note.Opacity == 1 && note.Bounds.Width == 380).ConfigureAwait(true);
                CaptureMotionRegion(window, root, 840, -674, 404, 70, $"selection-copy-note-{theme}-settled");
                Assert.Equal(selected!.Trim(), await window.Clipboard!.TryGetTextAsync().ConfigureAwait(true));
                var dismiss = note.FindControl<Button>("Dismiss")!;
                var dismissAt = dismiss.TranslatePoint(new Point(13, 12), window)!.Value;
                window.MouseDown(dismissAt, MouseButton.Left);
                window.MouseUp(dismissAt, MouseButton.Left);
                Assert.Empty(notices.Children);
            }
            else
            {
                Assert.Contains($"> {selected!.Trim()}", window.FindControl<TextBox>("Composer")!.Text!, StringComparison.Ordinal);
            }
        },
        allowMotion: true);
}
