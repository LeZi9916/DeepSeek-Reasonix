using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Shell.Settings;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks the provider row's reference 120ms hover background fade.</summary>
    /// <returns>The provider hover transition check.</returns>
    [Fact]
    public Task ProviderRowsFadeTheirHoverBackground() =>
        WithSettingsAsync(
            "conversation",
            (window, _) =>
            {
                Navigate(window, "settings-model");
                var row = window.GetVisualDescendants().OfType<ProviderAccountRow>().First();
                var frame = row.FindControl<Border>("Frame")!;
                var transition = Assert.Single(frame.Transitions!.OfType<BrushTransition>());
                Assert.Equal(TimeSpan.FromMilliseconds(120), transition.Duration);
                Assert.IsType<SplineEasing>(transition.Easing);
                return Task.CompletedTask;
            });

    /// <summary>Checks the provider rail row's reference card entrance.</summary>
    /// <returns>The provider-row entrance check.</returns>
    [Fact]
    public Task ProviderRailRowsEnterWithTheReferenceCardMotion() =>
        WithSettingsAsync(
            "conversation",
            async (window, _) =>
            {
                await WaitForSettingsFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
                Navigate(window, "settings-model");
                var rail = window.GetVisualDescendants().OfType<ModelServices>().Single().FindControl<StackPanel>("ProviderRail")!;
                var row = rail.GetVisualDescendants().OfType<ProviderAccountRow>().First();
                await WaitForSettingsFrame(() => row.Opacity is > 0 and < 0.999 && row.RenderTransform!.Value.M32 > 0.4).ConfigureAwait(true);
                await WaitForSettingsFrame(() => row.Opacity == 1 && row.RenderTransform!.Value.M32 == 0).ConfigureAwait(true);
            },
            allowMotion: true);

    /// <summary>Keeps provider rows at their final pose when motion is off.</summary>
    /// <returns>The reduced-motion provider check.</returns>
    [Fact]
    public Task ProviderRailRowsHonorReducedMotion() =>
        WithSettingsAsync(
            "conversation",
            (window, _) =>
            {
                Navigate(window, "settings-model");
                var rail = window.GetVisualDescendants().OfType<ModelServices>().Single().FindControl<StackPanel>("ProviderRail")!;
                var row = rail.GetVisualDescendants().OfType<ProviderAccountRow>().First();
                Assert.Equal(1d, row.Opacity);
                Assert.Equal(0d, row.RenderTransform?.Value.M32 ?? 0);
                return Task.CompletedTask;
            });
}
