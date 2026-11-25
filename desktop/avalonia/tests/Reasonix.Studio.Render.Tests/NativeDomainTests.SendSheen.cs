using Avalonia;
using Avalonia.Controls;
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
}
