using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks the Electron send button's brightness filter and visible ink.</summary>
    /// <param name="dark">Whether to use the dark palette.</param>
    /// <returns>The send button's painted colours across hover and exit.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SendHoverUsesReferenceBrightnessWithoutSweep(bool dark) => WithSettingsAsync(
        "conversation",
        async (window, _) =>
        {
            await WaitForSettingsFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
            window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var send = window.FindControl<Button>("Send")!;
            send.IsEnabled = true;
            window.UpdateLayout();
            Assert.Null(window.FindControl<Border>("SendSheen"));
            var baseColor = dark ? "#D8D9D4" : "#252725";
            var hoverColor = dark ? "#FFFFFA" : "#2C2E2C";
            var icon = Assert.Single(send.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>());
            Assert.Equal(Color.Parse(baseColor), Assert.IsAssignableFrom<ISolidColorBrush>(send.Background).Color);
            window.MouseMove(send.TranslatePoint(new Point(8, 8), window)!.Value);
            Assert.Equal(Color.Parse(hoverColor), Assert.IsAssignableFrom<ISolidColorBrush>(send.Background).Color);
            Assert.Equal(Color.Parse(dark ? "#1B1C1B" : "#FFFFFF"), Assert.IsAssignableFrom<ISolidColorBrush>(icon.Stroke).Color);
            window.MouseMove(new Point(620, 160));
            Assert.Equal(Color.Parse(baseColor), Assert.IsAssignableFrom<ISolidColorBrush>(send.Background).Color);
        },
        allowMotion: true);
}
