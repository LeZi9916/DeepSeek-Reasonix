using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Wire;
using Reasonix.Studio.Pages;
using Reasonix.Studio.Shell.Settings;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Observes the visible progress of a real tool disclosure through the exit.</summary>
    /// <returns>The close-frame check.</returns>
    [Fact]
    public Task ToolDisclosureDoesNotReopenBeforeItHides() => WithSettingsAsync(
        "conversation",
        async (w, _) =>
        {
            await WaitForMotionFrame(() => w.Classes.Contains("motion")).ConfigureAwait(true);
            w.ShowState(new SessionState
            {
                Items = new TranscriptItem[]
                {
                    new SayItem { Id = "answer", Done = true, Text = "Answer" },
                    new ToolItem { Id = "tool", Call = new Tool { Name = "bash", Output = "tool output" } },
                },
            });
            w.UpdateLayout();
            var page = w.FindControl<SessionPage>("Conversation")!;
            var activity = page.GetVisualDescendants().OfType<Control>().First(control => control.GetType().Name == "ActivityDisclosure");
            var group = activity.FindControl<Control>("Body")!;
            if (!group.IsVisible)
            {
                activity.FindControl<Button>("Toggle")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }

            await WaitForMotionFrame(() => group.IsVisible && group.Opacity == 1).ConfigureAwait(true);
            w.UpdateLayout();
            var tool = activity.GetVisualDescendants().OfType<Control>().Single(control => control.GetType().Name == "ToolCallHeader");
            var body = tool.FindControl<Control>("Detail")!;
            var toggle = tool.FindControl<Button>("Toggle")!;
            var progress = AvaloniaPropertyRegistry.Instance.FindRegistered(body, "Progress")!;
            if (!body.IsVisible)
            {
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }

            await WaitForMotionFrame(() => body.IsVisible && (double)body.GetValue(progress)! == 1).ConfigureAwait(true);
            w.UpdateLayout();
            Assert.True(body.Bounds.Height > 0);
            var samples = new List<double>();
            body.PropertyChanged += (_, change) =>
            {
                if (change.Property == progress && body.IsVisible)
                {
                    samples.Add((double)body.GetValue(progress)!);
                }
            };
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForMotionFrame(() => !body.IsVisible).ConfigureAwait(true);
            Assert.Contains(samples, value => value is > 0 and < 1);
            Assert.Equal(samples.OrderDescending(), samples);
        },
        allowMotion: true);

    /// <summary>Transfers a live disclosure to its virtualized row at the activity boundary.</summary>
    /// <returns>The streaming fold interaction.</returns>
    [Fact]
    public Task ToolDisclosureContinuesWhenActivityStartsVirtualizing() => WithSettingsAsync(
        "conversation",
        async (w, _) =>
        {
            await WaitForMotionFrame(() => w.Classes.Contains("motion")).ConfigureAwait(true);
            var answer = new SayItem { Id = "answer", Done = true, Text = "Read the workspace" };
            var output = string.Join("\n", Enumerable.Repeat("File contents", 20));
            var tools = Enumerable.Range(0, 25).Select(index => new ToolItem
            {
                Id = $"fold-tool-{index}",
                Call = new Tool { Name = "read_file", Output = output },
            }).ToArray();
            var start = new SessionState
            {
                RunSince = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Running = true,
                Items = [answer, .. tools.Take(24)],
            };
            w.ShowState(start);
            w.UpdateLayout();
            var page = w.FindControl<SessionPage>("Conversation")!;
            var group = page.GetVisualDescendants().OfType<Control>().First(control => control.GetType().Name == "ActivityDisclosure");
            var groupBody = group.FindControl<Control>("Body")!;
            if (!groupBody.IsVisible)
            {
                group.FindControl<Button>("Toggle")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }

            await WaitForMotionFrame(() => groupBody.IsVisible && groupBody.Opacity == 1).ConfigureAwait(true);
            w.UpdateLayout();
            var before = FirstTool();
            var detail = before.FindControl<Control>("Detail")!;
            var toggle = before.FindControl<Button>("Toggle")!;
            var progress = AvaloniaPropertyRegistry.Instance.FindRegistered(detail, "Progress")!;
            if (detail.IsVisible)
            {
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitForMotionFrame(() => !detail.IsVisible).ConfigureAwait(true);
            }

            detail.GetType().GetProperty("DurationMs")!.SetValue(detail, 750d);
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForMotionFrame(() => (double)detail.GetValue(progress)! is > 0.05 and < 0.9).ConfigureAwait(true);
            var fraction = (double)detail.GetValue(progress)!;
            w.ShowState(start with
            {
                Items = [answer, .. tools],
            });
            w.UpdateLayout();
            var replacement = FirstTool();
            Assert.NotSame(before, replacement);
            var continued = replacement.FindControl<Control>("Detail")!;
            Assert.InRange((double)continued.GetValue(progress)!, fraction - 0.001, 0.999);
            await WaitForMotionFrame(() => (double)continued.GetValue(progress)! == 1).ConfigureAwait(true);

            Control FirstTool() => page.GetVisualDescendants().OfType<Border>()
                .First(border => border.Tag is ToolItem { Id: "fold-tool-0" }).Child!;
        },
        allowMotion: true);

    /// <summary>Exercises the preview modes and native transcript through the same settings entry point.</summary>
    /// <param name="width">The viewport width.</param><param name="dark">Whether to use dark appearance.</param>
    /// <returns>The settings interaction and captures.</returns>
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(760, false)]
    [InlineData(760, true)]
    public Task TranscriptFoldingPreviewAndConversationFollowTheSelectedMode(int width, bool dark) => WithSettings("settings-display", (w, _) =>
    {
        var height = width == 1440 ? 900 : 480;
        w.Width = width;
        w.Height = height;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();
        var scroll = w.FindControl<ScrollViewer>("SettingsScroller")!;
        Assert.True(ControlAt<RadioButton>(w, "action:transcript.fold-preset.standard").IsChecked);
        Capture("standard");
        var thinking = ControlAt<RadioButton>(w, "action:transcript.fold.thinking.folded").GetVisualAncestors().OfType<FoldingOptionRow>().Single();
        w.MouseMove(thinking.TranslatePoint(new Point(17, 12), w)!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(w.GetVisualDescendants().OfType<Border>(), border => border.Name == "ThoughtFrame" && border.Classes.Contains("example-hot"));
        Capture("hover");
        w.MouseMove(new Point(5, 35));
        ClickAt(w, "action:transcript.fold-preview.live");
        Assert.Contains(VisibleText(), text => text == "思考中…");
        Capture("live");
        ClickAt(w, "action:transcript.fold-preview.done");
        ClickAt(w, "action:transcript.fold-preset.full");
        Assert.Contains(VisibleText(), text => text == "npm test");
        Capture("full");
        ClickAt(w, "action:transcript.fold-preset.changes");
        Capture("changes");
        ClickAt(w, "action:transcript.fold-preset.compact");
        Capture("compact");
        ClickAt(w, "action:transcript.fold.thinking.open");
        ClickAt(w, "action:transcript.fold.output.open");
        Assert.Contains("自定义", VisibleText());
        Capture("custom");

        ClickAt(w, "action:transcript.fold-preset.full");
        ClickAt(w, "action:settings.close");
        var say = new SayItem { Id = "answer", Text = "answer", Reasoning = "a first thought\nMore reasoning", Done = true };
        w.ShowState(new SessionState
        {
            Items = new TranscriptItem[]
            {
                say,
                new ToolItem { Id = "step", Call = new Tool { Name = "bash", Output = "tool output" } },
                new CompactionItem { Id = "archive", Compaction = new Compaction { Summary = "archive summary" }, Done = true },
            },
        });
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var page = w.FindControl<SessionPage>("Conversation")!;
        Assert.True(page.GetVisualDescendants().OfType<SelectableTextBlock>().Single(text => text.Text == say.Reasoning).IsEffectivelyVisible);
        Assert.True(page.GetVisualDescendants().OfType<SelectableTextBlock>().Single(text => text.Text == "tool output").IsEffectivelyVisible);
        Assert.True(page.GetVisualDescendants().OfType<SelectableTextBlock>().Single(text => text.Text == "archive summary").IsEffectivelyVisible);

        var thought = page.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "Thought");
        thought.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Navigate(w, "settings-display");
        ClickAt(w, "action:transcript.fold-preset.standard");
        ClickAt(w, "action:transcript.fold-preset.full");
        ClickAt(w, "action:settings.close");
        Assert.False(page.GetVisualDescendants().OfType<SelectableTextBlock>().Single(text => text.Text == say.Reasoning).IsEffectivelyVisible);

        string?[] VisibleText() => w.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToArray();

        void Capture(string state)
        {
            scroll.Offset = new Vector(0, width == 760 || state is "full" or "changes" or "compact" or "custom" ? 280 : 0);
            Dispatcher.UIThread.RunJobs();
            CaptureFlow(w, $"settings-folding-{state}-{width}x{height}", resetScroll: false);
        }
    });

    /// <summary>Applies file-change expansion and lets the reader override the output clip.</summary>
    /// <returns>The native transcript interaction.</returns>
    [Fact]
    public Task TranscriptFoldingUsesReportedChangesAndKeepsTheOutputChoice() => WithSettings("settings-display", (w, _) =>
    {
        ClickAt(w, "action:transcript.fold-preset.changes");
        ClickAt(w, "action:settings.close");
        var longResult = string.Join("\n", Enumerable.Range(0, 40).Select(i => "updated line " + i));
        w.ShowState(new SessionState
        {
            Items = new TranscriptItem[]
            {
                new SayItem { Id = "answer", Text = "after", Done = true },
                new ToolItem { Id = "changed", Call = new Tool { Name = "edit_file", Diff = "+ approved text", Output = longResult } },
                new ToolItem { Id = "read", Call = new Tool { Name = "read_file", Output = "read result" } },
            },
        });
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var page = w.FindControl<SessionPage>("Conversation")!;
        Assert.DoesNotContain(page.GetVisualDescendants().OfType<SelectableTextBlock>(), text => text.Text == "read result" && text.IsEffectivelyVisible);
        var output = page.GetVisualDescendants().OfType<ScrollViewer>().Single(view => view.Name == "Output" && view.IsEffectivelyVisible);
        Assert.Equal(88, output.MaxHeight);
        Assert.True(output.Extent.Height > 88, $"output extent: {output.Extent}");
        var expand = output.GetVisualAncestors().OfType<UserControl>().First().FindControl<Button>("ExpandOutput")!;
        Assert.True(expand.IsEffectivelyVisible, $"clip button local visibility: {expand.IsVisible}; extent: {output.Extent}; viewport: {output.Viewport}");
        expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(double.PositiveInfinity, output.MaxHeight);

        Navigate(w, "settings-display");
        ClickAt(w, "action:transcript.fold-preset.standard");
        ClickAt(w, "action:transcript.fold-preset.changes");
        ClickAt(w, "action:settings.close");
        var reopened = page.GetVisualDescendants().OfType<ScrollViewer>().Single(view => view.Name == "Output" && view.IsEffectivelyVisible);
        Assert.Equal(double.PositiveInfinity, reopened.MaxHeight);
    });
}
