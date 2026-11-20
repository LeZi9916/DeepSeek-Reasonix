using Reasonix.Studio.Shell.Host;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the host port to the surface the page was written against, and the two
/// platform behaviours a shell owns itself: one instance, and one remembered window.
/// </summary>
/// <remarks>
/// The contract case is the one that matters for the migration: a member the page
/// calls and the shell does not answer is a control that silently does nothing, and
/// the compiler only catches it while the interface and the class are in one build.
/// </remarks>
public sealed class HostPortTests
{
    /// <summary>
    /// Checks that every member the interface declares is implemented.
    /// </summary>
    [Fact]
    public void EveryMemberIsImplemented()
    {
        var declared = typeof(IHostPort)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.MemberType is MemberTypes.Method or MemberTypes.Property)
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        // Every declared name must be found on the concrete type, because an
        // explicit or missing implementation is exactly the silent gap this test
        // exists to catch.
        foreach (var name in declared)
        {
            Assert.True(
                typeof(AvaloniaHost).GetMember(name, BindingFlags.Public | BindingFlags.Instance).Length > 0,
                $"{name} is declared on IHostPort and missing from AvaloniaHost");
        }
    }

    /// <summary>
    /// Checks that the platform name is one the page's selectors already use.
    /// </summary>
    [Fact]
    public void PlatformNameMatchesThePagesSpelling()
    {
        Assert.Contains(AvaloniaHost.PlatformName(), new[] { "windows", "darwin", "linux" });
    }

    /// <summary>
    /// Checks that a shell with no window answers the unsupported value instead of
    /// throwing, because a page may ask before the window exists.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task AShellWithoutAWindowAnswersUnsupported()
    {
        var host = new AvaloniaHost(null);

        Assert.False(host.CapturesWindow);
        Assert.Null(await host.CaptureWindowAsync());
        Assert.Null(await host.SaveTextAsync("note.txt", "body"));
        Assert.Null(await host.PickFolderAsync(string.Empty));
        Assert.Null(await host.ShowTextContextMenuAsync(
            new TextContextMenuRequest(true, false, false, false, false, 0, 0)));
    }

    /// <summary>
    /// Checks that a global shortcut is reported unsupported rather than silently
    /// claimed, because a binding that never fires reads as a broken setting.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task TheGlobalShortcutIsUnsupportedRatherThanPretended()
    {
        var host = new AvaloniaHost(null);

        Assert.Equal(GlobalShortcutStatus.Unsupported, (await host.GlobalShortcutAsync()).Status);
        Assert.Equal(GlobalShortcutStatus.Unsupported, (await host.SetGlobalShortcutAsync("Ctrl+Shift+R")).Status);
    }

    /// <summary>
    /// Checks that a path inside a workspace passes and a sibling that merely
    /// shares its prefix does not.
    /// </summary>
    [Fact]
    public void IsInsideDoesNotAcceptASiblingWithASharedPrefix()
    {
        var root = Path.Combine(Path.GetTempPath(), "reasonix-workspace");
        var inside = Path.Combine(root, "src", "main.go");
        var sibling = root + "-old";

        Assert.True(AvaloniaHost.IsInside(root, inside));
        Assert.True(AvaloniaHost.IsInside(root, root));
        Assert.False(AvaloniaHost.IsInside(root, sibling));
    }

    /// <summary>
    /// Checks that a second launch is refused while the first holds the name.
    /// </summary>
    [Fact]
    public void SecondLaunchIsRefused()
    {
        var identity = "test-" + Guid.NewGuid().ToString("N");

        using var first = new SingleInstance(identity);
        using var second = new SingleInstance(identity);

        Assert.True(first.IsFirst);
        Assert.False(second.IsFirst);
    }

    /// <summary>
    /// Checks that the window's geometry survives a save and a load.
    /// </summary>
    /// <param name="x">The left edge to remember.</param>
    /// <param name="maximised">Whether the window was maximised.</param>
    [Theory]
    [InlineData(120.0, false)]
    [InlineData(-40.0, true)]
    public void WindowGeometryRoundTrips(double x, bool maximised)
    {
        var path = Path.Combine(Path.GetTempPath(), "reasonix-window-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new WindowStateStore(path);
            store.Save(new WindowGeometry(x, 64, 1280, 720, maximised));

            var loaded = new WindowStateStore(path).Load();

            Assert.Equal(x, loaded.X);
            Assert.Equal(1280, loaded.Width);
            Assert.Equal(720, loaded.Height);
            Assert.Equal(maximised, loaded.Maximised);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Checks that a missing file opens the window at its default size rather than
    /// failing the launch.
    /// </summary>
    [Fact]
    public void AMissingGeometryOpensAtTheDefaultSize()
    {
        var path = Path.Combine(Path.GetTempPath(), "reasonix-window-" + Guid.NewGuid().ToString("N") + ".json");

        var loaded = new WindowStateStore(path).Load();

        Assert.Equal(WindowStateStore.DefaultWidth, loaded.Width);
        Assert.Equal(WindowStateStore.DefaultHeight, loaded.Height);
        Assert.Null(loaded.X);
    }
}
