using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Wire;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Shell.Workbench;
using Reasonix.Studio.Shell.Workspace;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Linq;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Exercises the search veil, setup step and composer focus motion through their normal inputs.</summary>
    /// <param name="dark">Whether to use the dark palette.</param>
    /// <returns>The shell motion check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ShellMotionFollowsFocusAndNavigation(bool dark) =>
        WithSettingsAsync(
            "conversation",
            async (w, reads) =>
            {
                w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                w.AdoptComposer(reads.Read<SessionStatus>("/status"));
                var scheme = dark ? "dark" : "light";
                var composer = w.FindControl<TextBox>("Composer")!;
                var surface = w.FindControl<StudioComposer>("ComposeSurface")!;
                var sweep = surface.GetVisualDescendants().OfType<ComposerSweep>().Single();
                Assert.Equal(0, sweep.Opacity);
                w.Classes.Add("motion");
                Assert.True(composer.Focus());
                Assert.True(surface.IsKeyboardFocusWithin);
                Assert.True(sweep.IsEffectivelyVisible);
                await WaitForMotionFrame(() => sweep.Opacity >= 0.22).ConfigureAwait(true);
                CaptureMotionRegion(w, surface, -8, 8, 782, 130, $"compose-{scheme}-418");
                await WaitForMotionFrame(() => sweep.Angle >= 505 && sweep.Opacity > 0.1).ConfigureAwait(true);
                CaptureMotionRegion(w, surface, -8, 8, 782, 130, $"compose-{scheme}-912");
                w.ShowState(new SessionState { Running = true });
                Assert.Equal(0, sweep.Opacity);
                w.ShowState(new SessionState
                {
                    Running = true,
                    Plan = new[]
                    {
                        new PlanStep { Text = "整理入口和约束", Status = TodoStatus.Completed },
                        new PlanStep { Text = "接入目前的进度", Status = TodoStatus.InProgress },
                        new PlanStep { Text = "核对截图和状态", Status = TodoStatus.Pending },
                    },
                });
                Dispatcher.UIThread.RunJobs();
                var plan = w.FindControl<Control>("ComposerPlan")!;
                var body = plan.FindControl<Border>("Body")!;
                ClickAt(w, "action:plan.fold");
                await WaitForMotionFrame(() => body.Opacity is >= 0.4 and < 0.999).ConfigureAwait(true);
                CaptureMotionRegion(w, surface, -8, 155, 760, 155, $"plan-{scheme}-entry");
                await WaitForMotionFrame(() => body.Opacity == 1).ConfigureAwait(true);
                CaptureMotionRegion(w, surface, -8, 155, 760, 155, $"plan-{scheme}-settled");
                ClickAt(w, "action:plan.collapse");
                await WaitForMotionFrame(() => !body.IsVisible).ConfigureAwait(true);
                w.ShowState(new SessionState());

                w.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.K,
                    KeyModifiers = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control,
                });
                var palette = w.GetVisualDescendants().OfType<WorkspaceSearchPanel>().Single();
                await WaitForMotionFrame(() => palette.Opacity is > 0 and < 0.999).ConfigureAwait(true);
                Assert.True(palette.RenderTransform!.Value.M31 > 0);
                var sheet = palette.FindControl<Border>("Sheet")!;
                CaptureMotionRegion(w, sheet, -16 - (int)Math.Floor(palette.RenderTransform.Value.M31), 8, 640, 100, $"palette-{scheme}-entry");
                await WaitForMotionFrame(() => palette.Opacity == 1).ConfigureAwait(true);
                CaptureMotionRegion(w, sheet, -16, 8, 640, 100, $"palette-{scheme}-settled");
                palette.Hide();

                w.FindControl<MenuItem>("WorkbenchView")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                if (w.FindControl<Control>("WorkbenchStart")!.IsEffectivelyVisible)
                {
                    ClickAt(w, "action:workbench.launch-files");
                }

                var files = w.FindControl<ListBox>("WorkspaceFiles")!;
                var tabs = w.FindControl<StackPanel>("FileTabs")!;
                files.SelectedItem = files.Items.Cast<object>().Single(file => file.ToString() == "README.md");
                await WaitForMotionFrame(() => tabs.Children.Any(control => Equals(control.Tag, "README.md"))).ConfigureAwait(true);
                var tab = tabs.Children.Single(control => Equals(control.Tag, "README.md"));
                if (tab.IsAnimating(Layoutable.MaxWidthProperty))
                {
                    CaptureMotionRegion(w, tabs, -60, 5, 350, 50, $"workbench-{scheme}-entry");
                }

                await WaitForMotionFrame(() => !tab.IsAnimating(Layoutable.MaxWidthProperty)).ConfigureAwait(true);
                CaptureMotionRegion(w, tabs, -60, 5, 350, 50, $"workbench-{scheme}-settled");
                Assert.IsType<WorkbenchHeaderTab>(tab).PickButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.False(tabs.Children.Single(control => Equals(control.Tag, "README.md")).IsAnimating(Layoutable.MaxWidthProperty));

                w.FindControl<Button>("OpenSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var settings = w.FindControl<Grid>("SettingsPanel")!;
                var header = settings.FindControl<TextBlock>("SettingsTitle")!;
                var settingsVeil = w.FindControl<Border>("SettingsVeil")!;
                var settingsSheet = w.FindControl<Grid>("SettingsSheet")!;
                var workspace = w.FindControl<Grid>("LayoutRoot")!;
                Assert.True(settings.IsVisible);
                if (settingsVeil.Opacity is > 0 and < 0.99)
                {
                    CaptureMotionRegion(w, header, -14, 8, 600, 100, $"settings-{scheme}-entry");
                }

                await WaitForMotionFrame(() => !workspace.IsVisible && !settingsSheet.IsAnimating(Visual.OpacityProperty)).ConfigureAwait(true);
                Assert.Equal(1, settingsVeil.Opacity);
                CaptureMotionRegion(w, header, -14, 8, 600, 100, $"settings-{scheme}-settled");

                w.ShowSetup(new ProviderSetup(true, true));
                Dispatcher.UIThread.RunJobs();
                var welcome = w.FindControl<Grid>("WelcomeContent")!;
                var mark = w.FindControl<Control>("WelcomeMark")!;
                var fraction = AvaloniaPropertyRegistry.Instance.FindRegistered(mark, "Fraction")!;
                await WaitForMotionFrame(() => mark.IsAnimating(fraction) && (double)mark.GetValue(fraction)! > 0.85).ConfigureAwait(true);
                CaptureMotionRegion(w, welcome, -6, 0, 470, 325, $"welcome-{scheme}-entry");
                await WaitForMotionFrame(() => !w.FindControl<TextBlock>("SetupTime")!.IsAnimating(Visual.OpacityProperty)).ConfigureAwait(true);
                CaptureMotionRegion(w, welcome, -6, 0, 470, 325, $"welcome-{scheme}-settled");
                w.FindControl<Button>("BeginSetup")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var card = w.FindControl<StackPanel>("SetupCard")!;
                await WaitForMotionFrame(() => card.Opacity is > 0 and < 0.999).ConfigureAwait(true);
                Assert.True(card.RenderTransform!.Value.M31 > 0);
                CaptureMotionRegion(w, card, -16, 8, 590, 130, $"onboarding-{scheme}-entry");
                await WaitForMotionFrame(() => card.Opacity == 1).ConfigureAwait(true);
                CaptureMotionRegion(w, card, -16, 8, 590, 130, $"onboarding-{scheme}-settled");
                Assert.Equal(0, card.RenderTransform!.Value.M31);
            },
            allowMotion: true);
}
