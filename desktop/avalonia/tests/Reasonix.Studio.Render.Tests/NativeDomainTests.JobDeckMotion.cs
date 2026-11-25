using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks the background-job deck row's reference card entrance.</summary>
    /// <param name="dark">Whether the conversation uses the dark theme.</param>
    /// <returns>The job-row entrance check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task BackgroundJobRowEntersWithTheReferenceCardMotion(bool dark) =>
        WithSettingsAsync(
            "conversation",
            async (window, _) =>
            {
                window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                await WaitForSettingsFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
                window.AdoptComposer(Status());
                window.UpdateLayout();
                var chip = window.FindControl<UserControl>("BackgroundJobs")!;
                var row = chip.FindControl<StackPanel>("Rows")!.Children.OfType<Border>().First(border => Equals(border.Tag, "job-motion"));
                await WaitForSettingsFrame(() => row.Opacity is > 0 and < 0.999 && row.RenderTransform!.Value.M32 > 0.4).ConfigureAwait(true);
                await WaitForSettingsFrame(() => row.Opacity == 1 && row.RenderTransform!.Value.M32 == 0).ConfigureAwait(true);
            },
            allowMotion: true);

    /// <summary>Keeps the job deck row at its final pose when motion is off.</summary>
    /// <returns>The reduced-motion job-row check.</returns>
    [Fact]
    public Task BackgroundJobRowHonorsReducedMotion() =>
        WithSettingsAsync(
            "conversation",
            (window, _) =>
            {
                window.AdoptComposer(Status());
                window.UpdateLayout();
                var chip = window.FindControl<UserControl>("BackgroundJobs")!;
                var row = chip.FindControl<StackPanel>("Rows")!.Children.OfType<Border>().First(border => Equals(border.Tag, "job-motion"));
                Assert.Equal(1d, row.Opacity);
                Assert.Equal(0d, row.RenderTransform?.Value.M32 ?? 0);
                return Task.CompletedTask;
            });

    /// <summary>Builds one running job for the deck fixtures.</summary>
    /// <returns>The session status.</returns>
    private static Reasonix.Studio.Kernel.Client.SessionStatus Status() =>
        JsonSerializer.Deserialize<Reasonix.Studio.Kernel.Client.SessionStatus>("""
            {"sessionPath":"/sessions/jobs.jsonl","jobs":[{"id":"job-motion","label":"Build","description":"Build","status":"running"}]}
            """)!;
}
