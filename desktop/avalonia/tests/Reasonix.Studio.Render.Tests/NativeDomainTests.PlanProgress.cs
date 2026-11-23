using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks the settings preference against the actual composer plan strip and its layout.</summary>
    /// <param name="width">The reference viewport width.</param><param name="dark">Whether to use dark appearance.</param>
    /// <returns>The mounted interactions and capture states.</returns>
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(760, false)]
    [InlineData(760, true)]
    public Task PlanProgressSettingControlsOnlyTheCollapsedIndicators(int width, bool dark) => WithSettings("conversation", (w, _) =>
    {
        var height = width == 1440 ? 900 : 480;
        w.Width = width;
        w.Height = height;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var state = new SessionState
        {
            Plan = new[]
            {
                new PlanStep { Text = "整理入口和约束", Status = TodoStatus.Completed },
                new PlanStep { Text = "接入目前的进度", Status = TodoStatus.InProgress, ActiveForm = "正在接入目前的进度" },
                new PlanStep { Text = "核对截图和状态", Status = TodoStatus.Pending, Level = 1 },
            },
        };
        w.ShowState(state);
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var compact = ControlAt<Button>(w, "action:plan.fold");
        Assert.Equal(34, compact.Bounds.Height);
        var compose = w.FindControl<Control>("ComposeSurface")!;
        var top = compose.TranslatePoint(default, w)!.Value.Y;
        Assert.InRange(compact.TranslatePoint(default, w)!.Value.Y + compact.Bounds.Height, top - 0.5, top + 0.5);
        var progress = ControlAt<ProgressBar>(w, "action:plan.progress");
        Assert.Equal(1, progress.Value);
        Assert.Equal(3, progress.Maximum);
        CaptureFlow(w, $"settings-plan-collapsed-{width}x{height}");

        ClickAt(w, "action:plan.fold");
        Assert.False(compact.IsEffectivelyVisible);
        Assert.False(progress.IsEffectivelyVisible);
        Assert.True(ControlAt<Button>(w, "action:plan.collapse").IsEffectivelyVisible);
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "核对截图和状态" && t.IsEffectivelyVisible);
        Assert.InRange(compose.TranslatePoint(default, w)!.Value.Y, top - 0.5, top + 0.5);
        CaptureFlow(w, $"settings-plan-expanded-{width}x{height}");
        w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        w.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Dispatcher.UIThread.RunJobs();
        Assert.True(compact.IsEffectivelyVisible);
        Assert.True(compact.IsFocused);

        w.ShowState(state with
        {
            Running = true,
        });
        Dispatcher.UIThread.RunJobs();
        CaptureFlow(w, $"settings-plan-running-{width}x{height}");

        Navigate(w, "settings-display");
        var toggle = ControlAt<ToggleSwitch>(w, "action:appearance.plan-progress");
        Assert.True(toggle.IsChecked);
        var scroll = w.FindControl<ScrollViewer>("SettingsScroller")!;
        scroll.Offset = new Vector(0, scroll.Offset.Y + toggle.TranslatePoint(default, scroll)!.Value.Y - 135);
        CaptureFlow(w, $"settings-plan-setting-on-{width}x{height}", resetScroll: false);
        toggle.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        CaptureFlow(w, $"settings-plan-setting-{width}x{height}", resetScroll: false);
        var optionRow = toggle.GetVisualAncestors().OfType<Border>().First(border => Equals(border.Tag, "plan.show_collapsed_plan_progress"));
        CaptureFlow(w, $"settings-plan-setting-row-{width}x{height}", resetScroll: false, region: optionRow);
        ClickAt(w, "action:settings.close");
        w.ShowState(state);
        Dispatcher.UIThread.RunJobs();
        Assert.True(compact.IsEffectivelyVisible);
        Assert.False(progress.IsEffectivelyVisible);
        CaptureFlow(w, $"settings-plan-progress-off-{width}x{height}");
        ClickAt(w, "action:plan.fold");
        Assert.True(ControlAt<Button>(w, "action:plan.collapse").IsEffectivelyVisible);

        w.ShowState(state with
        {
            Items = new TranscriptItem[] { new ApprovalItem { Id = "waiting", Approval = new() } },
        });
        Dispatcher.UIThread.RunJobs();
        Assert.False(compact.IsEffectivelyVisible);
        w.ShowState(state);
        Dispatcher.UIThread.RunJobs();
        Assert.True(compact.IsEffectivelyVisible);
    });

    /// <summary>Interrupts the opening motion with a close and exercises the final restored focus.</summary>
    /// <returns>The native animation lifecycle.</returns>
    [Fact]
    public Task PlanProgressFoldSettlesAfterAnInterruptedAnimation() => WithSettings("conversation", (w, _) =>
    {
        w.Classes.Add("motion");
        w.ShowState(new SessionState
        {
            Plan = new[] { new PlanStep { Text = "Plan", Status = TodoStatus.InProgress } },
        });
        Dispatcher.UIThread.RunJobs();
        var summary = ControlAt<Button>(w, "action:plan.fold");
        Assert.True(summary.Focus());
        ClickAt(w, "action:plan.fold");
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        Assert.False(summary.IsEffectivelyVisible);
        ClickAt(w, "action:plan.collapse");
        PumpNavigation(() =>
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            return summary.IsEffectivelyVisible && summary.IsFocused;
        });
    });
}
