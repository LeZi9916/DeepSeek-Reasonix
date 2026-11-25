using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks the docked rail's reference width and opacity motion through its normal toggles.</summary>
    /// <param name="dark">Whether the conversation uses the dark theme.</param>
    /// <returns>The rail motion check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DockedRailFadesAndResizesOnToggle(bool dark) =>
        WithSettingsAsync(
            "conversation",
            async (window, _) =>
            {
                window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                await WaitForSettingsFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
                var rail = window.FindControl<Control>("Rail")!;
                var layout = window.FindControl<Grid>("LayoutRoot")!;
                var collapse = window.FindControl<Button>("CollapseRail")!;
                var expand = window.FindControl<Button>("ExpandRail")!;
                var width = window.RailWidth;

                collapse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(rail.IsVisible);
                await WaitForSettingsFrame(() => rail.Width is > 1 and < 263 && rail.Opacity is > 0 and < 0.99).ConfigureAwait(true);
                CaptureMotionRegion(window, rail, 0, 0, 300, 220, $"rail-close-{(dark ? "dark" : "light")}");
                await WaitForSettingsFrame(() => !rail.IsVisible).ConfigureAwait(true);
                Assert.Equal(0, layout.ColumnDefinitions[0].Width.Value, 2);
                Assert.True(expand.IsVisible);

                expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(rail.IsVisible);
                await WaitForSettingsFrame(() => rail.Width > 1 && rail.Width < width && rail.Opacity is > 0 and < 0.99).ConfigureAwait(true);
                await WaitForSettingsFrame(() => rail.Width == width && rail.Opacity == 1).ConfigureAwait(true);
                Assert.Equal(width, layout.ColumnDefinitions[0].Width.Value, 2);
            },
            allowMotion: true);

    /// <summary>Keeps the rail toggle instant when reduced motion is in force.</summary>
    /// <returns>The reduced-motion rail check.</returns>
    [Fact]
    public Task DockedRailHonorsReducedMotion() =>
        WithSettingsAsync(
            "conversation",
            (window, _) =>
            {
                var rail = window.FindControl<Control>("Rail")!;
                var layout = window.FindControl<Grid>("LayoutRoot")!;
                window.FindControl<Button>("CollapseRail")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(rail.IsVisible);
                Assert.Equal(0, layout.ColumnDefinitions[0].Width.Value, 2);
                return Task.CompletedTask;
            });
}
