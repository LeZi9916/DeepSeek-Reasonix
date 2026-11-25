using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks the send button's reference hover sheen.</summary>
    /// <returns>The send-sheen check.</returns>
    [Fact]
    public Task SendSheenCrossesTheButtonOnHover() =>
        WithSettingsAsync(
            "conversation",
            async (window, _) =>
            {
                await WaitForSettingsFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
                window.FindControl<Button>("Send")!.IsEnabled = true;
                var sheen = window.FindControl<Border>("SendSheen")!;
                window.PlaySendSheen();
                await WaitForSettingsFrame(() => sheen.Opacity == 1 && sheen.RenderTransform!.Value.M31 is < 90 and > -90).ConfigureAwait(true);
                await WaitForSettingsFrame(() => sheen.Opacity == 0).ConfigureAwait(true);
            },
            allowMotion: true);

    /// <summary>Fades an in-progress shine when the pointer leaves Send.</summary>
    /// <returns>The real hover and exit interaction.</returns>
    [Fact]
    public Task SendSheenFadesOnPointerExit() =>
        WithSettingsAsync(
            "conversation",
            async (window, _) =>
            {
                await WaitForSettingsFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
                var send = window.FindControl<Button>("Send")!;
                send.IsEnabled = true;
                window.UpdateLayout();
                var sheen = window.FindControl<Border>("SendSheen")!;
                window.MouseMove(send.TranslatePoint(new Point(8, 8), window)!.Value);
                await WaitForSettingsFrame(() => sheen.Opacity == 1 && sheen.RenderTransform!.Value.M31 is < 80 and > -70).ConfigureAwait(true);
                var faded = false;
                sheen.PropertyChanged += (_, change) =>
                {
                    if (change.Property == Visual.OpacityProperty && sheen.Opacity is > 0 and < 1)
                    {
                        faded = true;
                    }
                };
                window.MouseMove(new Point(600, 140));
                await WaitForSettingsFrame(() => sheen.Opacity == 0).ConfigureAwait(true);
                Assert.True(faded);
            },
            allowMotion: true);
}
