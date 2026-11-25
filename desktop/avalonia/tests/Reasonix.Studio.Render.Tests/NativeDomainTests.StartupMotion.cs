using Avalonia.Controls;
using Avalonia.Styling;
using Reasonix.Studio.Shell.Startup;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Exercises the full entrance and departure sequence over a mounted native page.</summary>
    /// <param name="dark">Whether the landing uses the dark palette.</param>
    /// <returns>The startup intro check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task StartupMotionDrawsReferencePhases(bool dark) => WithSettingsAsync(
        "conversation",
        async (window, _) =>
        {
            window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var scene = window.FindControl<BootIntro>("StartupIntro")!;
            var scheme = dark ? "dark" : "light";
            Assert.False(scene.IsVisible);
            scene.IsVisible = true;
            scene.Begin(motion: true, english: false);
            await WaitForMotionFrame(() => scene.EntranceTime >= 390).ConfigureAwait(true);
            CaptureMotionRegion(window, scene, 565, -165, 770, 385, $"boot-intro-{scheme}-390");
            await WaitForMotionFrame(() => scene.EntranceTime >= 720).ConfigureAwait(true);
            CaptureMotionRegion(window, scene, 410, -275, 620, 340, $"boot-intro-{scheme}-720");
            scene.Progress(2);
            await WaitForMotionFrame(() => scene.EntranceTime >= 1300).ConfigureAwait(true);
            CaptureMotionRegion(window, scene, 410, -275, 620, 340, $"boot-intro-{scheme}-1300");
            Assert.True(double.IsNaN(scene.DepartureTime));
            scene.Progress(3);
            scene.Complete();
            await WaitForMotionFrame(() => scene.DepartureTime >= 270).ConfigureAwait(true);
            CaptureMotionRegion(window, scene, 410, -275, 620, 340, $"boot-leave-{scheme}-270");
            Assert.False(scene.IsHitTestVisible);
            var sweep = window.FindControl<BootChromeSweep>("StartupChromeSweep")!;
            Assert.InRange(sweep.Phase, 0.1, 0.8);
            await WaitForMotionFrame(() => scene.DepartureTime >= 780).ConfigureAwait(true);
            CaptureMotionRegion(window, scene, 410, -275, 620, 340, $"boot-leave-{scheme}-780");
            await WaitForMotionFrame(() => scene.DepartureTime >= 1200).ConfigureAwait(true);
            CaptureMotionRegion(window, scene, 410, -275, 620, 340, $"boot-leave-{scheme}-1200");
            await WaitForMotionFrame(() => !scene.IsVisible).ConfigureAwait(true);

            // Render the isolated title-bar highlight with the page fully visible, as in the reference capture.
            sweep.Phase = 140d / 1150;
            CaptureMotionRegion(window, window.FindControl<Grid>("LayoutRoot")!, 410, 0, 700, 75, $"boot-chrome-{scheme}-140");
            sweep.ClearValue(BootChromeSweep.PhaseProperty);
        },
        allowMotion: true);

    /// <summary>Checks the reduced-motion fade and that skipping still waits for a page.</summary>
    /// <param name="motion">Whether to play the full entrance before skipping.</param>
    /// <returns>The startup handoff check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task StartupMotionWaitsForFirstPage(bool motion) => WithSettingsAsync(
        "conversation",
        async (window, _) =>
        {
            var scene = window.FindControl<BootIntro>("StartupIntro")!;
            scene.IsVisible = true;
            scene.Begin(motion, english: true);
            await WaitForMotionFrame(() => scene.EntranceTime >= 90).ConfigureAwait(true);
            Assert.Equal(motion, scene.CanSkip);
            scene.Skip();
            if (motion)
            {
                Assert.False(scene.CanSkip);
                Assert.Equal(1730, scene.EntranceTime);
            }

            await Task.Delay(70).ConfigureAwait(true);
            Assert.True(double.IsNaN(scene.DepartureTime));
            Assert.True(scene.IsVisible);
            Assert.True(scene.IsHitTestVisible);
            scene.Complete();
            await WaitForMotionFrame(() => scene.DepartureTime >= 0).ConfigureAwait(true);
            await WaitForMotionFrame(() => !scene.IsVisible).ConfigureAwait(true);
        });
}
