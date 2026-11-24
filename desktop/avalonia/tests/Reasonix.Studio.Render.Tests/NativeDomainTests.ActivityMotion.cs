using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Wire;
using Reasonix.Studio.Pages;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Linq;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Animates live dispatch, settlement, failure and a folded running caption.</summary>
    /// <param name="dark">Whether to use the dark theme.</param>
    /// <returns>The tool group motion check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ActivityMotionFollowsLiveToolTransitions(bool dark) =>
        WithSettingsAsync(
            "conversation",
            async (w, _) =>
            {
                w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                await WaitForMotionFrame(() => w.Classes.Contains("motion")).ConfigureAwait(true);
                var since = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var question = new UserItem { Id = "q", MsgIndex = 1, Text = "检查工作区中这份文件。" };
                var state = new SessionState
                {
                    Running = true,
                    RunSince = since,
                    Items = new TranscriptItem[] { question },
                };
                var scheme = dark ? "dark" : "light";
                w.ShowState(state);

                var tool = new ToolItem
                {
                    Id = "tool-1",
                    Running = true,
                    Call = new Tool { ID = "tool-1", Name = "bash", Args = "{\"command\":\"cat README.md\"}", StartedAt = since },
                };
                w.ShowState(state with
                {
                    Items = new TranscriptItem[] { question, tool },
                });
                w.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var header = Header();
                await WaitForMotionFrame(() => header.Opacity is > 0.66 and < 0.995).ConfigureAwait(true);
                CaptureMotionRegion(w, header, -4, 4, 390, 54, $"activity-step-{scheme}-50");
                await WaitForMotionFrame(() => header.Opacity == 1).ConfigureAwait(true);

                var toggle = Group();
                toggle.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                var live = toggle.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "ActivityNow");
                Assert.True(live.IsEffectivelyVisible);
                Assert.Contains("cat README.md", live.Text, StringComparison.Ordinal);
                var brush = Assert.IsType<LinearGradientBrush>(live.Foreground);
                await WaitForMotionFrame(() => brush.StartPoint.Point.X is >= -0.85 and < 0.2).ConfigureAwait(true);
                CaptureMotionRegion(w, toggle, -4, 4, 760, 42, $"activity-shimmer-{scheme}-800");
                toggle.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();

                var finished = tool with
                {
                    Running = false,
                    Call = tool.Call with
                    {
                        Output = "README.md",
                    },
                };
                w.ShowState(state with
                {
                    Items = new TranscriptItem[] { question, finished },
                });
                w.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var settled = Header().GetVisualDescendants().OfType<Viewbox>().Single(icon => icon.Name == "StaticStatus");
                await WaitForMotionFrame(() => settled.RenderTransform!.Value.M11 > 1.08).ConfigureAwait(true);
                CaptureMotionRegion(w, Header(), -4, 4, 760, 54, $"activity-pop-{scheme}-110");

                var failing = tool with
                {
                    Id = "tool-2",
                    Call = tool.Call with
                    {
                        ID = "tool-2",
                        Args = "{\"command\":\"cat absent.txt\"}",
                    },
                };
                w.ShowState(state with
                {
                    Items = new TranscriptItem[] { question, finished, failing },
                });
                var failed = failing with
                {
                    Running = false,
                    Call = failing.Call with
                    {
                        Err = "not found",
                    },
                };
                w.ShowState(state with
                {
                    Items = new TranscriptItem[] { question, finished, failed },
                });
                w.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                header = Header();
                await WaitForMotionFrame(() => Math.Abs(header.RenderTransform!.Value.M31) > 0.5).ConfigureAwait(true);
                CaptureMotionRegion(w, header, -4, 4, 390, 54, $"activity-shake-{scheme}-45");

                Control Header() => w.GetVisualDescendants().OfType<UserControl>().Last(control => control.GetType().Name == "ToolCallHeader");
                Button Group() => w.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("activity-group"));
            },
            allowMotion: true);
}
