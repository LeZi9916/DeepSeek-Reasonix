using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Uses the independent transcript jump controls at both ends of an overflowing conversation.</summary>
    /// <param name="width">Window width, including a layout without the mark rail.</param>
    /// <returns>The mounted conversation interaction.</returns>
    [Theory]
    [InlineData(760)]
    [InlineData(1440)]
    public Task TranscriptJumpButtonsTrackReachAndReturnToLiveTail(int width) => WithSettings("conversation", (window, _) =>
    {
        window.Width = width;
        var page = window.FindControl<SessionPage>("Conversation")!;
        var scroll = page.GetVisualDescendants().OfType<ScrollViewer>().First();
        var items = Enumerable.Range(0, 18).Select(index => (TranscriptItem)new SayItem
        {
            Id = $"answer-{index}",
            Text = string.Join("\n\n", Enumerable.Repeat($"Paragraph {index}", 10)),
            Done = true,
        }).ToArray();
        window.ShowState(new SessionState { Items = items });
        Settle();
        var up = Jump("up");
        var down = Jump("down");
        Assert.False(up.IsEffectivelyVisible);
        Assert.True(down.IsEffectivelyVisible);
        Click(down);
        Assert.InRange(scroll.ScrollBarMaximum.Y - scroll.Offset.Y, 0, 2);
        Assert.True(up.IsEffectivelyVisible);
        Assert.False(down.IsEffectivelyVisible);

        var next = new SayItem { Id = "latest", Done = true, Text = string.Join("\n\n", Enumerable.Repeat("A new paragraph", 8)) };
        window.ShowState(new SessionState { Items = [.. items, next] });
        Settle();
        Assert.InRange(scroll.ScrollBarMaximum.Y - scroll.Offset.Y, 0, 2);
        Click(up);
        Assert.Equal(0, scroll.Offset.Y);
        Assert.True(down.IsEffectivelyVisible);

        window.ShowState(new SessionState());
        Settle();
        Assert.False(up.IsEffectivelyVisible);
        Assert.False(down.IsEffectivelyVisible);

        Button Jump(string direction) => page.GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetAutomationId(button) == $"action:transcript.scroll.{direction}");

        void Click(Button button)
        {
            var point = button.TranslatePoint(new Point(14, 14), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Settle();
        }

        void Settle()
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    });

    /// <summary>Steps to expanded reasoning and restores its position after another transcript is shown.</summary>
    /// <returns>The native jump and retained reading location.</returns>
    [Fact]
    public Task TranscriptJumpStepsToVirtualizedThoughtAndRestoresReadingAnchor() => WithSettings("conversation", (window, _) =>
    {
        var page = window.FindControl<SessionPage>("Conversation")!;
        var scroll = page.GetVisualDescendants().OfType<ScrollViewer>().First();
        page.SetTranscriptFolding(new TranscriptFolding(Thinking: TranscriptFoldMode.Open));
        var original = page.DisclosureChoices;
        var items = Enumerable.Range(0, 35).Select(index => (TranscriptItem)new SayItem
        {
            Id = $"answer-{index}",
            Text = string.Join("\n\n", Enumerable.Repeat($"Paragraph {index}", 6)),
            Done = true,
            Reasoning = index == 9 ? "the expanded thought" : null,
        }).ToArray();
        var state = new SessionState { Items = items };
        window.ShowState(state);
        Settle();
        var down = page.GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetAutomationId(button) == "action:transcript.scroll.down");
        var point = down.TranslatePoint(new Point(14, 14), window)!.Value;
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.Shift);
        Settle();
        Assert.InRange(scroll.Offset.Y, 48, scroll.ScrollBarMaximum.Y - 48);
        var thought = page.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "Thought" && button.IsEffectivelyVisible);
        Assert.InRange(thought.TranslatePoint(default, scroll)!.Value.Y, 6, 10);
        var position = page.ReadSessionPosition();
        Assert.NotNull(position);

        page.DisclosureChoices = new TranscriptDisclosureChoices();
        window.ShowState(new SessionState { Items = items[..22] });
        page.RestoreSessionPosition(null);
        Settle();
        Assert.InRange(scroll.ScrollBarMaximum.Y - scroll.Offset.Y, 0, 2);
        page.DisclosureChoices = original;
        window.ShowState(state);
        page.RestoreSessionPosition(position);
        Settle();
        Assert.Equal(position.Value.Id, page.ReadSessionPosition()?.Id);

        void Settle()
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    });
}
