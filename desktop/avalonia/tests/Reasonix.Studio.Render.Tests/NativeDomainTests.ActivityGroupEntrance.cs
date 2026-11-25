using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Wire;
using Reasonix.Studio.Pages;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks the activity group's reference card entrance.</summary>
    /// <param name="dark">Whether the conversation uses the dark theme.</param>
    /// <returns>The activity-group entrance check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ActivityGroupEntersWithTheReferenceCardMotion(bool dark) =>
        WithSettingsAsync(
            "conversation",
            async (window, _) =>
            {
                window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                await WaitForSettingsFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
                var question = new UserItem { Id = "group-question", MsgIndex = 1, Text = "Inspect the workspace." };
                var tool = new ToolItem
                {
                    Id = "group-tool",
                    Running = true,
                    Call = new Tool { ID = "group-tool", Name = "bash", Args = "{\"command\":\"cat README.md\"}", StartedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
                };
                window.ShowState(new SessionState
                {
                    Running = true,
                    Items = new TranscriptItem[] { question, tool },
                });
                window.UpdateLayout();
                var group = window.GetVisualDescendants().OfType<UserControl>().Single(control => control.GetType().Name == "ActivityDisclosure");
                await WaitForSettingsFrame(() => group.Opacity is > 0 and < 0.999 && group.RenderTransform!.Value.M32 > 0.4).ConfigureAwait(true);
                await WaitForSettingsFrame(() => group.Opacity == 1 && group.RenderTransform!.Value.M32 == 0).ConfigureAwait(true);
            },
            allowMotion: true);

    /// <summary>Keeps the activity group at its final pose when motion is off.</summary>
    /// <returns>The reduced-motion group check.</returns>
    [Fact]
    public Task ActivityGroupHonorsReducedMotion() =>
        WithSettingsAsync(
            "conversation",
            (window, _) =>
            {
                var question = new UserItem { Id = "still-question", MsgIndex = 1, Text = "Inspect." };
                var tool = new ToolItem
                {
                    Id = "still-tool",
                    Running = true,
                    Call = new Tool { ID = "still-tool", Name = "bash", Args = "{}", StartedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
                };
                window.ShowState(new SessionState
                {
                    Running = true,
                    Items = new TranscriptItem[] { question, tool },
                });
                window.UpdateLayout();
                var group = window.GetVisualDescendants().OfType<UserControl>().Single(control => control.GetType().Name == "ActivityDisclosure");
                Assert.Equal(1d, group.Opacity);
                Assert.Equal(0d, group.RenderTransform?.Value.M32 ?? 0);
                return Task.CompletedTask;
            });
}
