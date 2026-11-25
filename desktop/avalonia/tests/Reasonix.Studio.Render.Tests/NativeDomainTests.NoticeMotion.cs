using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks the kernel notice row's short reference entrance.</summary>
    /// <param name="dark">Whether the conversation uses the dark theme.</param>
    /// <returns>The notice entrance check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task NoticeRowEntersWithTheReferenceCardMotion(bool dark) =>
        WithSettingsAsync(
            "conversation",
            async (window, _) =>
            {
                window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                await WaitForSettingsFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
                var notice = new NoticeItem { Id = "notice-motion", Level = "warn", Text = "The host reported a warning." };
                window.ShowState(new SessionState { Items = new TranscriptItem[] { notice } });
                window.UpdateLayout();
                var row = window.GetVisualDescendants().OfType<Border>().Single(border => ReferenceEquals(border.Tag, notice));
                await WaitForSettingsFrame(() => row.Opacity is > 0 and < 0.999 && row.RenderTransform!.Value.M32 > 0.4).ConfigureAwait(true);
                await WaitForSettingsFrame(() => row.Opacity == 1 && row.RenderTransform!.Value.M32 == 0).ConfigureAwait(true);
            },
            allowMotion: true);

    /// <summary>Keeps the notice row at its final pose when motion is off.</summary>
    /// <returns>The reduced-motion notice check.</returns>
    [Fact]
    public Task NoticeRowHonorsReducedMotion() =>
        WithSettingsAsync(
            "conversation",
            (window, _) =>
            {
                var notice = new NoticeItem { Id = "notice-still", Level = "info", Text = "Notice." };
                window.ShowState(new SessionState { Items = new TranscriptItem[] { notice } });
                window.UpdateLayout();
                var row = window.GetVisualDescendants().OfType<Border>().Single(border => ReferenceEquals(border.Tag, notice));
                Assert.Equal(1d, row.Opacity);
                Assert.Equal(0d, row.RenderTransform?.Value.M32 ?? 0);
                return Task.CompletedTask;
            });
}
