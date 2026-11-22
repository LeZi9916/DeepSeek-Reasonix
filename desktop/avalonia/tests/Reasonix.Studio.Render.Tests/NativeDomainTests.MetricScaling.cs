using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Kernel.Client;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Native metric anchors keep their DIP geometry under Windows render scaling.</summary>
    /// <param name="scale">Windows scale factor.</param><param name="width">Viewport width.</param>
    /// <param name="height">Viewport height.</param><returns>The rendered layout and pointer check.</returns>
    [Theory]
    [InlineData(1.25, 1440, 900)]
    [InlineData(1.5, 1440, 900)]
    [InlineData(2, 1440, 900)]
    [InlineData(1.25, 760, 480)]
    [InlineData(1.5, 760, 480)]
    [InlineData(2, 760, 480)]
    public Task MetricCardsKeepDipGeometryAtWindowsScales(double scale, int width, int height) => WithSettings("conversation", (window, reads) =>
    {
        window.Width = width;
        window.Height = height;
        window.SetRenderScaling(scale);
        window.AdoptComposer(reads.Read<SessionStatus>("/status"));
        Dispatcher.UIThread.RunJobs();
        foreach (var name in new[] { "Speed", "Cache", "Context" })
        {
            var card = window.FindControl<StudioHoverCard>(name + "Hover")!;
            card.Open();
            Dispatcher.UIThread.RunJobs();
            var frame = card.FindControl<Border>("Frame")!;
            Assert.InRange(frame.Bounds.Width, name == "Speed" ? 267.5 : 359.5, name == "Speed" ? 268.5 : 360.5);
            var origin = frame.TranslatePoint(default, window)!.Value;
            Assert.InRange(origin.X, 0, width - frame.Bounds.Width + 0.5);
            Assert.InRange(origin.Y, 0, height - frame.Bounds.Height + 0.5);
            card.Close();
        }

        using var rendered = window.CaptureRenderedFrame();
        Assert.NotNull(rendered);
        Assert.Equal(PixelSize.FromSize(window.ClientSize, scale), rendered.PixelSize);
    });
}
