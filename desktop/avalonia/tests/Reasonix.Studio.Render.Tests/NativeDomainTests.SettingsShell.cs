using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Updates the theme mode and its local time fields through the appearance settings.</summary>
    /// <returns>The scheduled-theme interaction.</returns>
    [Fact]
    public Task ScheduledThemeShowsItsLocalTimeFields() => WithSettings("settings-appearance", (window, _) =>
    {
        var mode = window.GetVisualDescendants().OfType<RadioButton>()
            .Single(button => button.Tag is "Scheduled" && button.IsEffectivelyVisible);
        mode.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Scheduled", window.ThemeMode);
        var start = ControlAt<TimePicker>(window, "action:appearance.schedule.start");
        var end = ControlAt<TimePicker>(window, "action:appearance.schedule.end");
        Assert.Equal(TimeSpan.FromHours(20), start.SelectedTime);
        Assert.Equal(TimeSpan.FromHours(7), end.SelectedTime);
        start.SelectedTime = end.SelectedTime;
        Assert.Equal(Avalonia.Styling.ThemeVariant.Light, window.RequestedThemeVariant);

        var explicitDark = window.GetVisualDescendants().OfType<RadioButton>()
            .Single(button => button.Tag is "Dark" && AutomationProperties.GetAutomationId(button) == "action:appearance.scheme");
        explicitDark.IsChecked = true;
        Assert.Equal("Dark", window.ThemeMode);
        Assert.False(start.IsEffectivelyVisible);
    });

    /// <summary>Slides the settings sheet over the workspace and releases it after the exit animation.</summary>
    /// <returns>The mounted settings transition.</returns>
    [Fact]
    public Task SettingsSheetMovesIndependentlyFromItsVeil() =>
        WithSettingsAsync(
            "conversation",
            async (window, _) =>
            {
                await WaitForMotionFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
                var panel = window.FindControl<Grid>("SettingsPanel")!;
                var sheet = window.FindControl<Grid>("SettingsSheet")!;
                var veil = window.FindControl<Border>("SettingsVeil")!;
                var workspace = window.FindControl<Grid>("LayoutRoot")!;
                var translate = Assert.IsType<TranslateTransform>(sheet.RenderTransform);
                window.FindControl<Button>("OpenSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(panel.IsVisible);
                Assert.True(workspace.IsVisible);
                await WaitForMotionFrame(() => translate.X != 0 && veil.Opacity < 1).ConfigureAwait(true);
                await WaitForMotionFrame(() => !workspace.IsVisible).ConfigureAwait(true);
                Assert.Equal(1, sheet.Opacity);

                window.FindControl<Button>("CloseSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(panel.IsVisible);
                Assert.True(workspace.IsVisible);
                await WaitForMotionFrame(() => !panel.IsVisible).ConfigureAwait(true);
                Assert.Equal(0, translate.X);
                Assert.Equal(0, translate.Y);
            },
            allowMotion: true);
}
