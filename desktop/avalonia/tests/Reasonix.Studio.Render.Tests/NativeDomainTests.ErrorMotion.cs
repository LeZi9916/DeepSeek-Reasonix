using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks the error bar's reference card entrance.</summary>
    /// <param name="dark">Whether the conversation uses the dark theme.</param>
    /// <returns>The error-bar entrance check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ErrorBarEntersWithTheReferenceCardMotion(bool dark) =>
        WithSettingsAsync(
            "conversation",
            async (window, _) =>
            {
                window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                await WaitForSettingsFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
                window.ShowErrorBar("The host refused the request.");
                var bar = window.FindControl<Border>("ErrorBar")!;
                await WaitForSettingsFrame(() => bar.Opacity is > 0 and < 0.999 && bar.RenderTransform!.Value.M32 > 0.4).ConfigureAwait(true);
                await WaitForSettingsFrame(() => bar.Opacity == 1 && bar.RenderTransform!.Value.M32 == 0).ConfigureAwait(true);
            },
            allowMotion: true);

    /// <summary>Keeps the error bar at its final pose when motion is off.</summary>
    /// <returns>The reduced-motion error check.</returns>
    [Fact]
    public Task ErrorBarHonorsReducedMotion() =>
        WithSettingsAsync(
            "conversation",
            (window, _) =>
            {
                window.ShowErrorBar("The host refused the request.");
                var bar = window.FindControl<Border>("ErrorBar")!;
                Assert.True(bar.IsVisible);
                Assert.Equal(1d, bar.Opacity);
                Assert.Equal(0d, bar.RenderTransform?.Value.M32 ?? 0);
                return Task.CompletedTask;
            });
}
