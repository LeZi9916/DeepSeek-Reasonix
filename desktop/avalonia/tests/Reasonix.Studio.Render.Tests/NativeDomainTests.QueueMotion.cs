using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks that only newly admitted queue entries replay the reference card entrance.</summary>
    /// <param name="dark">Whether the conversation uses the dark theme.</param>
    /// <returns>The queue entrance check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task QueueRowsEnterOnlyWhenTheyArrive(bool dark) =>
        WithSettingsAsync(
            "conversation",
            async (window, reads) =>
            {
                window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                await WaitForSettingsFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
                reads.Exchange = request => request.RequestUri!.AbsolutePath == "/inbox"
                    ? Task.FromResult<HttpResponseMessage?>(JsonReply(JsonNode.Parse("""
                        {"revision":1,"paused":false,"items":[{"id":"queue-motion","preview":"queued line","intent":"followup","state":"queued"}],"capacity":{}}
                        """)!.AsObject()))
                    : Task.FromResult<HttpResponseMessage?>(null);

                await window.RefreshQueueAsync().ConfigureAwait(true);
                var strip = window.FindControl<StackPanel>("QueueStrip")!;
                var first = Assert.IsType<Border>(strip.Children[0]);
                Assert.Equal("queue-motion", first.Tag);
                await WaitForSettingsFrame(() => first.Opacity is > 0 and < 0.999 && first.RenderTransform!.Value.M32 > 0.4).ConfigureAwait(true);
                await WaitForSettingsFrame(() => first.Opacity == 1 && first.RenderTransform!.Value.M32 == 0).ConfigureAwait(true);

                await window.RefreshQueueAsync().ConfigureAwait(true);
                var second = Assert.IsType<Border>(strip.Children[0]);
                Assert.Equal("queue-motion", second.Tag);
                Assert.Equal(1d, second.Opacity);
                Assert.Equal(0d, second.RenderTransform?.Value.M32 ?? 0);
            },
            allowMotion: true);

    /// <summary>Keeps queue rows at their final pose when motion is off.</summary>
    /// <returns>The reduced-motion queue check.</returns>
    [Fact]
    public Task QueueRowsHonorReducedMotion() =>
        WithSettingsAsync(
            "conversation",
            async (window, reads) =>
            {
                reads.Exchange = request => request.RequestUri!.AbsolutePath == "/inbox"
                    ? Task.FromResult<HttpResponseMessage?>(JsonReply(JsonNode.Parse("""
                        {"revision":1,"paused":false,"items":[{"id":"queue-still","preview":"queued line","intent":"followup","state":"queued"}],"capacity":{}}
                        """)!.AsObject()))
                    : Task.FromResult<HttpResponseMessage?>(null);
                await window.RefreshQueueAsync().ConfigureAwait(true);
                var row = Assert.IsType<Border>(window.FindControl<StackPanel>("QueueStrip")!.Children[0]);
                Assert.Equal(1d, row.Opacity);
                Assert.Equal(0d, row.RenderTransform?.Value.M32 ?? 0);
            });

    /// <summary>Checks the queue row's reference 180ms hover background fade.</summary>
    /// <returns>The queue hover transition check.</returns>
    [Fact]
    public Task QueueRowsFadeTheirHoverBackground() =>
        WithSettingsAsync(
            "conversation",
            async (window, reads) =>
            {
                reads.Exchange = request => request.RequestUri!.AbsolutePath == "/inbox"
                    ? Task.FromResult<HttpResponseMessage?>(JsonReply(JsonNode.Parse("""
                        {"revision":1,"paused":false,"items":[{"id":"queue-hover","preview":"queued line","intent":"followup","state":"queued"}],"capacity":{}}
                        """)!.AsObject()))
                    : Task.FromResult<HttpResponseMessage?>(null);
                await window.RefreshQueueAsync().ConfigureAwait(true);
                var row = Assert.IsType<Border>(window.FindControl<StackPanel>("QueueStrip")!.Children[0]);
                var transition = Assert.Single(row.Transitions!.OfType<BrushTransition>());
                Assert.Equal(TimeSpan.FromMilliseconds(180), transition.Duration);
                Assert.IsType<SplineEasing>(transition.Easing);
            });

    /// <summary>Checks the queue action buttons' reference 120ms hover fades.</summary>
    /// <returns>The queue action transition check.</returns>
    [Fact]
    public Task QueueActionButtonsFadeTheirHoverStates() =>
        WithSettingsAsync(
            "conversation",
            async (window, reads) =>
            {
                reads.Exchange = request => request.RequestUri!.AbsolutePath == "/inbox"
                    ? Task.FromResult<HttpResponseMessage?>(JsonReply(JsonNode.Parse("""
                        {"revision":1,"paused":false,"items":[{"id":"queue-buttons","preview":"queued line","intent":"followup","state":"queued"}],"capacity":{}}
                        """)!.AsObject()))
                    : Task.FromResult<HttpResponseMessage?>(null);
                await window.RefreshQueueAsync().ConfigureAwait(true);
                var row = Assert.IsType<Border>(window.FindControl<StackPanel>("QueueStrip")!.Children[0]);
                var button = row.GetVisualDescendants().OfType<Button>().First(item => item.Classes.Contains("compose-queue-action"));
                var transitions = button.Transitions!.OfType<BrushTransition>().ToArray();
                Assert.Equal(2, transitions.Length);
                Assert.All(transitions, transition =>
                {
                    Assert.Equal(TimeSpan.FromMilliseconds(120), transition.Duration);
                    Assert.IsType<SplineEasing>(transition.Easing);
                });
            });
}
