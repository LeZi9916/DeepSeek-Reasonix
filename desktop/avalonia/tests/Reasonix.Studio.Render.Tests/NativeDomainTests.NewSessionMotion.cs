using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks the new-session button's reference hover fade and return.</summary>
    /// <param name="dark">Whether the conversation uses the dark theme.</param>
    /// <returns>The hover check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task NewSessionFadesItsHoverBackground(bool dark) =>
        WithSettingsAsync(
            "conversation",
            async (window, _) =>
            {
                window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                await WaitForMotionFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
                var button = window.FindControl<Button>("NewSession")!;
                var before = Ink(button);
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point);
                await WaitForMotionFrame(() => Ink(button) != before).ConfigureAwait(true);
                window.MouseMove(new Point(5, 5));
                await WaitForMotionFrame(() => Ink(button) == before).ConfigureAwait(true);

                static Color Ink(Button button) => Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color;
            },
            allowMotion: true);
}
