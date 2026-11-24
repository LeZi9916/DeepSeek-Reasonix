using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Wire;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks the count change, redraw continuity and reduced-motion behavior.</summary>
    /// <param name="dark">Whether to use the dark palette.</param>
    /// <returns>The activity count motion check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ActivityCountRollsOnlyWhenTheVisibleCountChanges(bool dark) =>
        WithSettingsAsync(
            "conversation",
            async (w, _) =>
            {
                w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                await WaitForMotionFrame(() => w.Classes.Contains("motion")).ConfigureAwait(true);
                var since = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var question = new UserItem { Id = "count-q", MsgIndex = 3, Text = "核对操作计数" };
                var read = new ToolItem
                {
                    Id = "count-1",
                    Call = new Tool { ID = "count-1", Name = "read_file", Args = "{\"path\":\"README.md\"}", Output = "README.md" },
                };
                var edit = new ToolItem
                {
                    Id = "count-2",
                    Call = new Tool { ID = "count-2", Name = "edit_file", Args = "{\"path\":\"README.md\"}", StartedAt = since },
                    Running = true,
                };
                var first = new SessionState { Running = true, RunSince = since, Items = new TranscriptItem[] { question, read } };
                var second = first with
                {
                    Items = new TranscriptItem[] { question, read, edit },
                };
                w.ShowState(first);
                w.UpdateLayout();
                Assert.Equal("1 项操作", Count().Text);
                Assert.False(Offset().IsAnimating(TranslateTransform.YProperty));

                w.ShowState(second);
                w.UpdateLayout();
                var rolling = Offset();
                await WaitForMotionFrame(() => rolling.IsAnimating(TranslateTransform.YProperty)).ConfigureAwait(true);
                Assert.Equal("2 项操作", Count().Text);
                Assert.True(Count().GetVisualParent()!.ClipToBounds);
                Assert.Equal(12, Count().LineHeight);

                var changed = edit with
                {
                    Call = edit.Call with
                    {
                        Args = "{\"path\":\"NOTES.md\"}",
                    },
                };
                w.ShowState(second with
                {
                    Items = new TranscriptItem[] { question, read, changed },
                });
                w.UpdateLayout();
                Assert.Same(rolling, Offset());
                await WaitForMotionFrame(() => !rolling.IsAnimating(TranslateTransform.YProperty)).ConfigureAwait(true);
                Assert.Equal(0, Offset().Y);

                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STUDIO_MOTION_OUTPUT")))
                {
                    var scheme = dark ? "dark" : "light";
                    foreach (var (phase, minOffset, maxOffset) in new[] { (0, 11.999, 12.001), (45, 0.25, 2.1), (90, 0.001, 0.24) })
                    {
                        var captured = false;
                        for (var attempt = 0; attempt < 4 && !captured; attempt++)
                        {
                            w.ShowState(first);
                            w.ShowState(second);
                            w.UpdateLayout();
                            var sampled = Offset();
                            await WaitForMotionFrame(() =>
                                !sampled.IsAnimating(TranslateTransform.YProperty) || (sampled.Y >= minOffset && sampled.Y <= maxOffset)).ConfigureAwait(true);
                            if (sampled.Y >= minOffset && sampled.Y <= maxOffset)
                            {
                                CaptureMotionRegion(w, Group(), -4, 6, 780, 42, $"activity-count-{scheme}-{phase}");
                                captured = true;
                            }
                        }

                        Assert.True(captured, $"The renderer skipped the {phase} ms count sample.");
                    }

                    await WaitForMotionFrame(() => Offset().Y == 0).ConfigureAwait(true);
                    CaptureMotionRegion(w, Group(), -4, 6, 780, 42, $"activity-count-{scheme}-final");
                }

                w.ShowState(first);
                w.UpdateLayout();
                var interrupted = Offset();
                Assert.True(interrupted.IsAnimating(TranslateTransform.YProperty));
                w.Classes.Remove("motion");
                Assert.Equal(0, interrupted.Y);
                w.ShowState(second);
                w.UpdateLayout();
                Assert.Equal("2 项操作", Count().Text);
                Assert.False(Offset().IsAnimating(TranslateTransform.YProperty));

                Button Group() => w.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("activity-group"));
                TextBlock Count() => Group().GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "Summary");
                TranslateTransform Offset() => (TranslateTransform)Count().RenderTransform!;
            },
            allowMotion: true);
}
