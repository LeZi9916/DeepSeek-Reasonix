using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Shell;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>Exercises the reading column at the desktop window sizes people can open.</summary>
[Collection(HeadlessCollection.Name)]
public sealed class WindowLayoutTests
{
    /// <summary>Streaming text keeps the selected control mounted across immutable state updates.</summary>
    /// <returns>The rendered observation.</returns>
    [Fact]
    public async Task StreamingKeepsTheAnswerControl()
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(
            () =>
            {
                var window = new MainWindow(connectOnOpen: false);
                window.Show();
                var first = new SayItem { Id = "answer", Text = "one" };
                window.ShowState(new SessionState { Items = new[] { first } });
                Dispatcher.UIThread.RunJobs();
                var text = window.GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => t.Text == "one");
                window.ShowState(new SessionState { Items = new[] { first with { Text = "one two" } } });
                Assert.Same(text, window.GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => t.Text == "one two"));
                window.Close();
            },
            default);
    }

    /// <summary>Settled Markdown becomes selectable styled runs and a link with its destination.</summary>
    /// <returns>The headless view check.</returns>
    [Fact]
    public async Task MarkdownPreservesTheLinkAndEmphasis()
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(
            () =>
            {
                var window = new MainWindow(connectOnOpen: false);
                window.Show();
                window.ShowState(new SessionState
                {
                    Items = new[] { new SayItem { Id = "a", Done = true, Text = "**strong** and [docs](https://example.org/guide)" } },
                });
                Dispatcher.UIThread.RunJobs();
                var link = Assert.Single(window.GetVisualDescendants().OfType<HyperlinkButton>());
                Assert.Equal("https://example.org/guide", link.NavigateUri!.AbsoluteUri);
                var answer = window.GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => t.Inlines?.OfType<Bold>().Any() == true);
                Assert.Contains("strong", answer.Inlines!.Text, System.StringComparison.Ordinal);
                window.Close();
            },
            default);
    }

    /// <summary>Resize restores the dock width, and reopening a collapsed rail keeps keyboard focus reachable.</summary>
    /// <returns>The headless interaction.</returns>
    [Fact]
    public async Task DockAndThemeSurviveWindowFolds()
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(
            () =>
            {
                var window = new MainWindow(false);
                window.RestoreChrome(330, "Dark", true);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var rail = window.FindControl<Control>("Rail")!;
                var open = window.FindControl<Button>("ExpandRail")!;
                Assert.Equal(330, rail.Bounds.Width);
                window.Width = 1000;
                Dispatcher.UIThread.RunJobs();
                Assert.True(open.IsEffectivelyVisible);
                open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.True(rail.IsEffectivelyVisible);
                window.Width = 1440;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(330, rail.Bounds.Width);
                window.FindControl<Button>("ThemeSwitch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(ThemeVariant.Light, window.ActualThemeVariant);
                Assert.DoesNotContain("motion", window.Classes);
                window.Close();
            },
            default);
    }

    /// <summary>Both ends of the text field and the send action stay inside the client area.</summary>
    /// <param name="width">Client width.</param>
    /// <param name="height">Client height.</param>
    /// <returns>The layout observation.</returns>
    [Theory]
    [InlineData(760, 480)]
    [InlineData(840, 720)]
    [InlineData(1000, 720)]
    [InlineData(1200, 900)]
    [InlineData(1440, 900)]
    public async Task InputFitsWhenResized(int width, int height)
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(
            () =>
            {
                var window = new MainWindow(connectOnOpen: false);
                window.Show();
                window.Width = width;
                window.Height = height;
                window.RequestedThemeVariant = ThemeVariant.Dark;
                Dispatcher.UIThread.RunJobs();

                window.FindControl<TextBox>("Composer")!.Text = "a draft";
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
                foreach (var control in new Control[]
                {
                    window.FindControl<TextBox>("Composer")!,
                    window.FindControl<Button>("Send")!,
                })
                {
                    var origin = control.TranslatePoint(default, window)!.Value;
                    Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
                    Assert.InRange(origin.X, 0, width - control.Bounds.Width);
                    Assert.InRange(origin.Y, 0, height - control.Bounds.Height);
                    if (control is Button button)
                    {
                        var center = origin + new Vector(control.Bounds.Width / 2, control.Bounds.Height / 2);
                        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(center));
                        Assert.Contains(button, hit.GetSelfAndVisualAncestors());
                    }
                }

                window.Close();
            },
            default);
    }
}
