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
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
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
