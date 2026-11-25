using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Reasonix.Studio.Kernel.Client;
using System.Collections.Generic;
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

    /// <summary>Reverses a visible exit from its current position and settles before hiding.</summary>
    /// <param name="width">The window width for docked or overlay layout.</param>
    /// <returns>The workbench reversal and exit regression.</returns>
    [Theory]
    [InlineData(1440)]
    [InlineData(760)]
    public Task WorkbenchReversesItsSlideWithoutFlashing(int width) =>
        WithSettingsAsync(
            "conversation",
            async (window, _) =>
            {
                window.Width = width;
                window.UpdateLayout();
                var root = window.FindControl<Grid>("LayoutRoot")!;
                await WaitForMotionFrame(() => window.Classes.Contains("motion") && root.Bounds.Width == width).ConfigureAwait(true);
                var panel = window.FindControl<Border>("WorkbenchPanel")!;
                var toggle = window.FindControl<Button>("ViewSwitch")!;
                var entersAsDock = width == 1440;
                if (!entersAsDock)
                {
                    toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    window.FindControl<Button>("FullWorkbench")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.True(double.IsNaN(panel.Width));
                    toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await WaitForMotionFrame(() => !panel.IsVisible).ConfigureAwait(true);
                }

                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var opening = new List<double>();
                var openingProperty = entersAsDock ? Layoutable.WidthProperty : Visual.OpacityProperty;
                panel.PropertyChanged += ObserveOpening;
                await WaitForMotionFrame(() => panel.IsVisible && panel.Opacity == 1 && panel.Bounds.Width > 100
                    && !panel.IsAnimating(Layoutable.WidthProperty) && !panel.IsAnimating(Visual.OpacityProperty)).ConfigureAwait(true);
                panel.PropertyChanged -= ObserveOpening;
                Assert.Contains(opening, amount => amount > 0 && amount < (entersAsDock ? panel.Width : 1));
                Assert.Equal(opening.Order().ToArray(), opening.ToArray());

                void ObserveOpening(object? sender, AvaloniaPropertyChangedEventArgs change)
                {
                    if (change.Property == openingProperty)
                    {
                        opening.Add(entersAsDock ? panel.Width : panel.Opacity);
                    }
                }

                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitForMotionFrame(() => panel.Opacity is > 0.2 and < 0.8).ConfigureAwait(true);
                var opacity = panel.Opacity;
                var offset = panel.RenderTransform!.Value.M31;
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(opacity, panel.Opacity, 2);
                Assert.Equal(offset, panel.RenderTransform!.Value.M31, 2);
                await WaitForMotionFrame(() => panel.Opacity == 1 && panel.RenderTransform!.Value.M31 == 0).ConfigureAwait(true);

                var samples = new List<double>();
                panel.PropertyChanged += (_, change) =>
                {
                    if (change.Property == Visual.OpacityProperty && panel.IsVisible)
                    {
                        samples.Add(panel.Opacity);
                    }
                };
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitForMotionFrame(() => !panel.IsVisible).ConfigureAwait(true);
                Assert.Contains(samples, amount => amount is > 0 and < 1);
                Assert.Equal(samples.OrderDescending().ToArray(), samples.ToArray());
                Assert.True(window.FindControl<TextBox>("Composer")!.IsFocused);

                window.Classes.Remove("motion");
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(panel.IsVisible);
                Assert.Equal(1, panel.Opacity);
                Assert.Equal(0, panel.RenderTransform!.Value.M31);
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(panel.IsVisible);
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
