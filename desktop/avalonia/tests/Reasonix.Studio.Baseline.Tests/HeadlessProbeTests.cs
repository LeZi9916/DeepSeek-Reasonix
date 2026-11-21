using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Shell;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the one fact the geometry gate depends on: a real Avalonia window can be
/// laid out under a headless session from a plain xUnit v2 case.
/// </summary>
/// <remarks>
/// Nothing here is about Studio's own layout. It answers whether the session API,
/// the application's resource dictionary and a laid-out visual tree are available
/// in the test process at all; a gate built on an assumption it cannot make would
/// otherwise report zero observations and call that agreement.
/// </remarks>
[Collection(HeadlessCollection.Name)]
public sealed class HeadlessProbeTests
{
    /// <summary>
    /// Checks that a window holding a Studio control lays out with a real size.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task AStudioControlLaysOutUnderAHeadlessSession()
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(App));

        var size = await session.Dispatch(
            () =>
            {
                var window = new Window
                {
                    Width = 1440,
                    Height = 900,
                    Content = new StudioRail
                    {
                        Content = new TextBlock { Text = "probe" },
                    },
                };

                window.Show();

                return window.Bounds.Size;
            },
            default);

        Assert.True(size.Width > 0, "the window reported no width");
        Assert.True(size.Height > 0, "the window reported no height");
    }

    /// <summary>
    /// Checks that the token dictionary the controls read is resolvable from the
    /// application, which is what a template binding silently falls back from.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task TheTokenDictionaryResolvesInTheHeadlessApplication()
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(App));

        var hasToken = await session.Dispatch(
            () =>
            {
                if (Application.Current is not { } app)
                {
                    return false;
                }

                Assert.True(app.TryGetResource("StudioLightPrimaryBrush", Avalonia.Styling.ThemeVariant.Light, out var light));
                Assert.Equal(Avalonia.Media.Color.FromArgb(36, 99, 128, 213), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(light).Color);
                return app.TryGetResource("RailW", app.ActualThemeVariant, out var value) && value is not null;
            },
            default);

        Assert.True(hasToken, "the rail width token did not resolve");
    }
}
