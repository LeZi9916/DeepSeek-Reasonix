using Avalonia;
using Reasonix.Studio.Shell.Host;
using System;
using System.Collections.Generic;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the two decisions a launch makes before anything is drawn: whether a
/// remembered window can still be seen, and how one shell finds the other.
/// </summary>
/// <remarks>
/// Both fail quietly when they are wrong. A window placed off-screen looks like a
/// shell that never started; an activation name that differs between two launches
/// looks like a second window that opened for no reason. Neither shows up in a
/// unit test of the window itself.
/// </remarks>
public sealed class ShellStartupTests
{
    /// <summary>
    /// The screen every case places windows on.
    /// </summary>
    private static readonly IReadOnlyList<PixelRect> s_screens = new[]
    {
        new PixelRect(0, 0, 1920, 1080),
        new PixelRect(1920, 0, 1280, 1024),
    };

    /// <summary>
    /// Checks that a window remembered inside a screen opens exactly there.
    /// </summary>
    [Fact]
    public void ARememberedPlaceOnAScreenIsKept()
    {
        var geometry = new WindowGeometry(200, 120, 1280, 720, Maximised: false);

        Assert.True(WindowPlacement.IsReachable(geometry, s_screens));
        Assert.Equal(new PixelPoint(200, 120), WindowPlacement.PositionOf(geometry, s_screens));
    }

    /// <summary>
    /// Checks that a place on a second monitor is kept too, because the monitor
    /// that was there yesterday may still be there today.
    /// </summary>
    [Fact]
    public void APlaceOnAnotherMonitorIsKept()
    {
        var geometry = new WindowGeometry(2000, 100, 1000, 800, Maximised: false);

        Assert.Equal(new PixelPoint(2000, 100), WindowPlacement.PositionOf(geometry, s_screens));
    }

    /// <summary>
    /// Checks that a place no screen covers is dropped, so the platform centres
    /// the window rather than drawing it where nobody can reach it.
    /// </summary>
    [Fact]
    public void APlaceOffEveryScreenIsDropped()
    {
        var gone = new WindowGeometry(9000, 9000, 1280, 720, Maximised: false);

        Assert.False(WindowPlacement.IsReachable(gone, s_screens));
        Assert.Null(WindowPlacement.PositionOf(gone, s_screens));
    }

    /// <summary>
    /// Checks that a window taller than a shrunken screen is still reachable when
    /// its title bar is, because that is the part a person drags.
    /// </summary>
    [Fact]
    public void ATooTallWindowIsStillReachableByItsBar()
    {
        var tall = new WindowGeometry(100, 10, 1200, 4000, Maximised: false);

        Assert.True(WindowPlacement.IsReachable(tall, s_screens));
    }

    /// <summary>
    /// Checks that a window whose top edge is just above a screen still counts,
    /// which is what a resolution change leaves behind.
    /// </summary>
    [Fact]
    public void ABarJustAboveTheScreenStillCounts()
    {
        var above = new WindowGeometry(100, -20, 1200, 800, Maximised: false);

        Assert.True(WindowPlacement.IsReachable(above, s_screens));
    }

    /// <summary>
    /// Checks that a window with no remembered position is not placed at all.
    /// </summary>
    [Fact]
    public void AWindowNeverPlacedIsNotPlacedByTheStore()
    {
        var unplaced = new WindowGeometry(null, null, 1280, 720, Maximised: false);

        Assert.False(WindowPlacement.IsReachable(unplaced, s_screens));
        Assert.Null(WindowPlacement.PositionOf(unplaced, s_screens));
    }

    /// <summary>
    /// Checks that both launches of one install build the same activation name,
    /// and that another install builds a different one.
    /// </summary>
    [Fact]
    public void OneInstallBuildsOneActivationName()
    {
        Assert.Equal(InstanceActivation.NameFor("abc"), InstanceActivation.NameFor("abc"));
        Assert.NotEqual(InstanceActivation.NameFor("abc"), InstanceActivation.NameFor("abd"));
        Assert.Equal("Reasonix.Studio.Activate.abc", InstanceActivation.NameFor("abc"));
    }

    /// <summary>
    /// Checks that the identity is stable within one install and short enough to
    /// name a mutex and an event.
    /// </summary>
    [Fact]
    public void TheIdentityIsStableAndShort()
    {
        var first = InstanceActivation.Identity();
        var second = InstanceActivation.Identity();

        Assert.Equal(first, second);
        Assert.Equal(16, first.Length);
        Assert.Equal(first.ToLowerInvariant(), first);
    }

    /// <summary>
    /// Checks that a launch that cannot raise the first does nothing rather than
    /// throwing, which is what happens when the first is already closing.
    /// </summary>
    [Fact]
    public void RaisingAVanishedShellDoesNothing()
    {
        InstanceActivation.Raise("test-" + Guid.NewGuid().ToString("N"));
    }
}
