using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Wire;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks the approval card's reference right-to-left entrance.</summary>
    /// <param name="dark">Whether the conversation uses the dark theme.</param>
    /// <returns>The prompt entrance check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ApprovalCardEntersFromTheReferenceOffset(bool dark) =>
        WithSettingsAsync(
            "conversation",
            async (window, _) =>
            {
                window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                await WaitForSettingsFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
                var approval = new ApprovalItem
                {
                    Id = "approval-motion",
                    Approval = new Approval { ID = "approval-motion", Tool = "bash", Subject = "npm test" },
                };
                window.ShowState(new SessionState { Items = new TranscriptItem[] { approval } });
                window.UpdateLayout();
                var row = window.GetVisualDescendants().OfType<Border>().Single(border => ReferenceEquals(border.Tag, approval));
                await WaitForSettingsFrame(() => row.Opacity is > 0 and < 0.999 && row.RenderTransform!.Value.M31 > 0.5).ConfigureAwait(true);
                await WaitForSettingsFrame(() => row.Opacity == 1 && row.RenderTransform!.Value.M31 == 0).ConfigureAwait(true);
            },
            allowMotion: true);

    /// <summary>Keeps the approval card at its final pose when motion is off.</summary>
    /// <returns>The reduced-motion prompt check.</returns>
    [Fact]
    public Task ApprovalCardHonorsReducedMotion() =>
        WithSettingsAsync(
            "conversation",
            (window, _) =>
            {
                var approval = new ApprovalItem
                {
                    Id = "approval-still",
                    Approval = new Approval { ID = "approval-still", Tool = "bash", Subject = "npm test" },
                };
                window.ShowState(new SessionState { Items = new TranscriptItem[] { approval } });
                window.UpdateLayout();
                var row = window.GetVisualDescendants().OfType<Border>().Single(border => ReferenceEquals(border.Tag, approval));
                Assert.Equal(1d, row.Opacity);
                Assert.Equal(0d, row.RenderTransform?.Value.M31 ?? 0);
                return Task.CompletedTask;
            });
}
