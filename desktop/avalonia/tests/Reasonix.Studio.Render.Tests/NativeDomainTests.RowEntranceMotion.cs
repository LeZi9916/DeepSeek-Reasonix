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
    /// <summary>Checks the new user-message row's reference card entrance.</summary>
    /// <param name="dark">Whether the conversation uses the dark theme.</param>
    /// <returns>The row entrance check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task UserMessageRowEntersWithTheReferenceCardMotion(bool dark) =>
        WithSettingsAsync(
            "conversation",
            async (window, _) =>
            {
                window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                await WaitForSettingsFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
                var user = new UserItem { Id = "user-motion", Text = "Hello" };
                window.ShowState(new SessionState { Items = new TranscriptItem[] { user } });
                window.UpdateLayout();
                var row = window.GetVisualDescendants().OfType<Border>().Single(border => ReferenceEquals(border.Tag, user));
                await WaitForSettingsFrame(() => row.Opacity is > 0 and < 0.999 && row.RenderTransform!.Value.M32 > 0.4).ConfigureAwait(true);
                await WaitForSettingsFrame(() => row.Opacity == 1 && row.RenderTransform!.Value.M32 == 0).ConfigureAwait(true);
            },
            allowMotion: true);

    /// <summary>Keeps the new message at its final pose when motion is off.</summary>
    /// <returns>The reduced-motion row check.</returns>
    [Fact]
    public Task UserMessageRowHonorsReducedMotion() =>
        WithSettingsAsync(
            "conversation",
            (window, _) =>
            {
                var user = new UserItem { Id = "user-still", Text = "Hello" };
                window.ShowState(new SessionState { Items = new TranscriptItem[] { user } });
                window.UpdateLayout();
                var row = window.GetVisualDescendants().OfType<Border>().Single(border => ReferenceEquals(border.Tag, user));
                Assert.Equal(1d, row.Opacity);
                Assert.Equal(0d, row.RenderTransform?.Value.M32 ?? 0);
                return Task.CompletedTask;
            });
}
