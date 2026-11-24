using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.IO;
using System.Linq;
using Xunit;
using Tool = Reasonix.Studio.Contracts.Wire.Tool;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks that streamed updates keep the live sweep and completion removes it.</summary>
    /// <param name="dark">Whether to use the dark palette.</param>
    /// <returns>The mounted transcript check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TranscriptMotionFollowsLiveState(bool dark) => WithSettingsAsync("conversation", async (w, _) =>
    {
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var page = w.FindControl<SessionPage>("Conversation")!;
        var thought = new SayItem { Id = "answer", Reasoning = "Check the existing implementation.", ThoughtStartedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        var state = new SessionState
        {
            Running = true,
            Items = new TranscriptItem[]
            {
                new UserItem { Id = "question", Text = "Inspect this change" },
                thought,
            },
        };
        w.ShowState(state);
        Dispatcher.UIThread.RunJobs();

        var label = page.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "ThoughtLabel");
        Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground);
        w.Classes.Add("motion");
        Dispatcher.UIThread.RunJobs();
        var brush = Assert.IsType<LinearGradientBrush>(label.Foreground);
        var scheme = dark ? "dark" : "light";
        CaptureMotionRegion(w, label, -14, 6, 155, 30, $"think-{scheme}-0");
        foreach (var (ms, position) in new[] { (650, -1.03125), (800, -0.75), (950, -0.46875), (1500, 0.5625) })
        {
            await WaitForMotionFrame(() => brush.StartPoint.Point.X >= position).ConfigureAwait(true);
            CaptureMotionRegion(w, label, -14, 6, 155, 30, $"think-{scheme}-{ms}");
        }

        w.ShowState(state with
        {
            Items = new[] { state.Items[0], thought with { Reasoning = thought.Reasoning + " Continue." } },
        });
        Assert.Same(brush, label.Foreground);
        Assert.DoesNotContain("0.0", label.Text, StringComparison.Ordinal);
        w.Classes.Remove("motion");
        Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground);
        w.Classes.Add("motion");
        Assert.Same(brush, label.Foreground);
        w.ShowState(state with
        {
            Running = false,
            Items = new[] { state.Items[0], thought with { Done = true } },
        });
        Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground);
        var outgoing = page.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "ThoughtOut");
        Assert.True(outgoing.IsEffectivelyVisible);
        w.Classes.Remove("motion");
        await WaitForMotionFrame(() => !outgoing.IsVisible).ConfigureAwait(true);
        w.Classes.Add("motion");

        var stream = thought with
        {
            Text = "The streaming caret follows this text.",
            ThoughtMs = 900,
        };
        w.ShowState(state with
        {
            Items = new[] { state.Items[0], stream },
        });
        Dispatcher.UIThread.RunJobs();
        var answerCaret = page.GetVisualDescendants().OfType<Control>().Single(control => control.Name == "AnswerCaret");
        Assert.True(answerCaret.IsEffectivelyVisible);
        var phase = AvaloniaPropertyRegistry.Instance.FindRegistered(answerCaret, "Phase")!;
        var answer = page.GetVisualDescendants().OfType<SelectableTextBlock>().Single(text => text.Name == "Answer");
        Assert.Equal(stream.Text, answer.Text);
        CaptureMotionRegion(w, answer, -4, 5, 290, 34, $"caret-{scheme}-0");
        await WaitForMotionFrame(() => (double)answerCaret.GetValue(phase)! >= 0.5).ConfigureAwait(true);
        CaptureMotionRegion(w, answer, -4, 5, 290, 34, $"caret-{scheme}-500");

        var runningTool = new ToolItem { Id = "call-1", Running = true, Call = new Tool { Name = "read_file", Args = """{"path":"README.md"}""" } };
        w.ShowState(state with
        {
            Items = new[] { state.Items[0], thought with { Done = true }, runningTool },
        });
        Dispatcher.UIThread.RunJobs();
        var runningHead = page.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("activity-toggle"));
        if (!page.GetVisualDescendants().OfType<Canvas>().Single(c => c.Name == "RunningStatus").IsEffectivelyVisible)
        {
            runningHead.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        var spinner = page.GetVisualDescendants().OfType<Canvas>().Single(c => c.Name == "RunningStatus");
        var rotationTransform = Assert.IsType<RotateTransform>(spinner.RenderTransform);
        Assert.True(spinner.IsEffectivelyVisible);
        w.Classes.Remove("motion");
        CaptureMotionRegion(w, spinner, -6, 6, 24, 24, $"spin-{scheme}-0");
        w.Classes.Add("motion");
        foreach (var ms in new[] { 150, 300, 450 })
        {
            await WaitForMotionFrame(() => rotationTransform.Angle >= ms * 0.6).ConfigureAwait(true);
            CaptureMotionRegion(w, spinner, -6, 6, 24, 24, $"spin-{scheme}-{ms}");
        }

        w.Classes.Remove("motion");
        Assert.Equal(0, rotationTransform.Angle);
        w.Classes.Add("motion");
        w.ShowState(state with
        {
            Items = new[] { state.Items[0], thought with { Done = true }, runningTool with { Running = false, Call = runningTool.Call with { Output = "README.md" } } },
        });
        Dispatcher.UIThread.RunJobs();
        var toggle = page.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("activity-toggle"));
        var chevron = toggle.GetVisualDescendants().OfType<Viewbox>().Single(v => v.Name == "Chevron");
        w.Classes.Remove("motion");
        if (chevron.Classes.Contains("open"))
        {
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        w.Classes.Add("motion");
        Dispatcher.UIThread.RunJobs();
        var body = page.GetVisualDescendants().OfType<Decorator>().Single(v => v.Name == "Body");
        var transition = Assert.IsType<TransformOperationsTransition>(Assert.Single(chevron.Transitions!));
        Assert.Equal(TimeSpan.FromMilliseconds(120), transition.Duration);
        Assert.Equal(-1, chevron.RenderTransform!.Value.M12, 5);
        CaptureMotionRegion(w, chevron, -6, 8, 95, 28, $"arrow-{scheme}-0");
        CaptureMotionRegion(w, chevron, -6, 8, 320, 96, $"disclosure-{scheme}-0");
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(-1, chevron.RenderTransform!.Value.M12, 5);
        Assert.True(body.IsVisible);
        Assert.Equal(0, body.Height);
        foreach (var (ms, rotation) in new[] { (30, -0.878), (60, -0.307), (119, -0.001) })
        {
            await WaitForMotionFrame(() => chevron.RenderTransform!.Value.M12 >= rotation).ConfigureAwait(true);
            CaptureMotionRegion(w, chevron, -6, 8, 95, 28, $"arrow-{scheme}-{ms}");
            CaptureMotionRegion(w, chevron, -6, 8, 320, 96, $"disclosure-{scheme}-{ms}");
        }

        Assert.True(body.Height > 0);
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        w.Classes.Remove("motion");
        Assert.False(body.IsVisible);
        Assert.True(double.IsNaN(body.Height));
        Assert.Null(chevron.Transitions);
        Assert.Equal(-1, chevron.RenderTransform!.Value.M12, 5);
    });

    /// <summary>Lets the render dispatcher advance to the requested motion state.</summary>
    /// <param name="ready">The sampled frame condition.</param>
    /// <returns>The wait for that state.</returns>
    private static async Task WaitForMotionFrame(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!ready() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(4).ConfigureAwait(true);
        }

        Assert.True(ready(), "The animation did not reach the sampled state.");
    }

    /// <summary>Writes a native crop and its reference anchor for visual comparison.</summary>
    /// <param name="w">The mounted window.</param>
    /// <param name="control">The stationary anchor for the crop.</param>
    /// <param name="dx">The horizontal crop offset.</param>
    /// <param name="top">The space above the anchor.</param>
    /// <param name="width">Crop width in DIPs.</param>
    /// <param name="height">Crop height in DIPs.</param>
    /// <param name="file">The effect, palette and frame being sampled.</param>
    private static void CaptureMotionRegion(Window w, Control control, int dx, int top, int width, int height, string file)
    {
        var output = Environment.GetEnvironmentVariable("STUDIO_MOTION_OUTPUT");
        if (string.IsNullOrEmpty(output))
        {
            return;
        }

        var location = control.GetVisualParent() is Control parent
            ? parent.TranslatePoint(control.Bounds.TopLeft, w)!.Value
            : control.TranslatePoint(default, w)!.Value;
        var clip = new Rect(Math.Floor(location.X) + dx, Math.Floor(location.Y) - top, width, height);
        using var full = new RenderTargetBitmap(PixelSize.FromSize(w.ClientSize, 1), new Vector(96, 96));
        full.Render(w);
        using var region = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        using (var drawing = region.CreateDrawingContext())
        {
            drawing.DrawImage(full, clip, new Rect(new Size(width, height)));
        }

        Directory.CreateDirectory(output);
        region.Save(Path.Combine(output, $"native-{file}.png"), PngBitmapEncoderOptions.Default);
        File.WriteAllText(Path.Combine(output, $"native-{file}.txt"), FormattableString.Invariant($"{location.X},{location.Y},{control.Bounds.Width},{control.Bounds.Height}"));
    }
}
