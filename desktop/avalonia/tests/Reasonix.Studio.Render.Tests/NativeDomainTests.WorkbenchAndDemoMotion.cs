using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Reasonix.Studio.Kernel.Client;
using System.Linq;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Closes the mounted workbench after its exit transition and restores focus.</summary>
    /// <param name="dark">Whether to use the dark palette.</param>
    /// <returns>The workbench dismissal check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task WorkbenchDismissSlidesBeforeReleasingSpace(bool dark) =>
        WithSettingsAsync(
            "workbench",
            async (w, _) =>
            {
                w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                var scheme = dark ? "dark" : "light";
                var panel = w.FindControl<Border>("WorkbenchPanel")!;
                var root = w.FindControl<Grid>("LayoutRoot")!;
                await WaitForMotionFrame(() => !panel.IsAnimating(Visual.OpacityProperty)).ConfigureAwait(true);
                var anchor = panel.TranslatePoint(default, root)!.Value;
                var tabs = w.FindControl<StackPanel>("FileTabs")!;
                var tab = tabs.Children.Single(control => Equals(control.Tag, "README.md"));
                await WaitForMotionFrame(() => !tab.IsAnimating(Visual.OpacityProperty)).ConfigureAwait(true);

                // Dispatch the click before pumping layout so the short exit can be sampled.
                ControlAt<Button>(w, "action:workbench.close").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Contains(tab, tabs.Children);
                await WaitForMotionFrame(() => tab.Opacity is > 0 and < 0.85).ConfigureAwait(true);
                Assert.True(tab.IsAnimating(Layoutable.MaxWidthProperty));
                CaptureMotionRegion(w, root, (int)anchor.X - 5, 5 - (int)anchor.Y, 350, 50, $"file-tab-out-{scheme}-75");
                await WaitForMotionFrame(() => !tabs.Children.Contains(tab)).ConfigureAwait(true);
                CaptureMotionRegion(w, root, (int)anchor.X - 5, 5 - (int)anchor.Y, 350, 50, $"file-tab-out-{scheme}-final");
                CaptureMotionRegion(w, root, (int)anchor.X - 5, 5 - (int)anchor.Y, 440, 140, $"workbench-out-{scheme}-0");
                ControlAt<Button>(w, "action:workbench.dismiss").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(panel.IsVisible);
                await WaitForMotionFrame(() => panel.Opacity is > 0 and < 0.9).ConfigureAwait(true);
                Assert.InRange(panel.RenderTransform!.Value.M31, 0.1, 24);
                CaptureMotionRegion(w, root, (int)anchor.X - 5, 5 - (int)anchor.Y, 440, 140, $"workbench-out-{scheme}-90");
                await WaitForMotionFrame(() => !panel.IsVisible).ConfigureAwait(true);
                Assert.True(w.FindControl<TextBox>("Composer")!.IsFocused);
                CaptureMotionRegion(w, root, (int)anchor.X - 5, 5 - (int)anchor.Y, 440, 140, $"workbench-out-{scheme}-final");
            },
            allowMotion: true);

    /// <summary>Pauses the timed onboarding example under the pointer and keeps the clicked final frame.</summary>
    /// <param name="dark">Whether to use the dark palette.</param>
    /// <returns>The welcome demo playback check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task OnboardingExamplePlaysPausesAndFinishes(bool dark) =>
        WithSettingsAsync(
            "conversation",
            async (w, _) =>
            {
                w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                w.ShowSetup(new ProviderSetup(true, true));
                Dispatcher.UIThread.RunJobs();
                var scheme = dark ? "dark" : "light";
                var demo = w.FindControl<Control>("SetupDemo")!;
                var thinking = demo.FindControl<StackPanel>("ThinkingRow")!;
                var read = demo.FindControl<Grid>("ReadRow")!;
                var edit = demo.FindControl<Grid>("EditRow")!;
                var result = demo.FindControl<Border>("ResultRow")!;
                await WaitForMotionFrame(() => thinking.Opacity >= 0.999).ConfigureAwait(true);
                Assert.Equal(0, read.Opacity);
                CaptureMotionRegion(w, demo, -4, 4, 328, 278, $"demo-{scheme}-1700");
                var pointer = demo.TranslatePoint(new Point(12, 12), w)!.Value;
                w.MouseMove(pointer);
                var paused = thinking.Opacity;
                await Task.Delay(350).ConfigureAwait(true);
                Assert.Equal(paused, thinking.Opacity);
                Assert.Equal(0, read.Opacity);
                w.MouseMove(new Point(15, 15));
                await WaitForMotionFrame(() => edit.Opacity >= 0.999).ConfigureAwait(true);
                Assert.Equal(0, thinking.Height);
                CaptureMotionRegion(w, demo, -4, 4, 328, 278, $"demo-{scheme}-4400");

                w.MouseMove(pointer);
                w.MouseDown(pointer, MouseButton.Left);
                w.MouseUp(pointer, MouseButton.Left);
                Assert.False(thinking.IsVisible);
                Assert.Equal(1, result.Opacity);
                await Task.Delay(100).ConfigureAwait(true);
                Assert.Equal(1, result.Opacity);
                CaptureMotionRegion(w, demo, -4, 4, 328, 278, $"demo-{scheme}-final");
            },
            allowMotion: true);
}
