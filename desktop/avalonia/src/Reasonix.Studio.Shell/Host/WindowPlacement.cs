using Avalonia;
using System;
using System.Collections.Generic;

namespace Reasonix.Studio.Shell.Host;

/// <summary>
/// Decides whether a remembered window still lands on a screen this launch can
/// see.
/// </summary>
/// <remarks>
/// A geometry read back on another monitor can sit entirely off-screen: the
/// numbers were right on the machine that wrote them and are furniture in the
/// void on this one. The window asks before it moves, and a place no screen
/// covers is dropped rather than clamped — clamping would stretch a small
/// remembered window across a large screen, and the person's own size is worth
/// more than a guess at where they meant to put it.
///
/// What must be reachable is the title bar, not the whole rectangle: a window
/// taller than a shrunken screen is still usable as long as its bar is on it.
/// </remarks>
public static class WindowPlacement
{
    /// <summary>
    /// How tall a window's title bar is, for the reachability check.
    /// </summary>
    private const int TitleBarHeight = 48;

    /// <summary>
    /// Reports whether a remembered position is reachable on at least one screen.
    /// </summary>
    /// <param name="geometry">The remembered window.</param>
    /// <param name="screens">Each screen's working area, in device pixels.</param>
    /// <returns>True when the window's title bar would be on a screen.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
    public static bool IsReachable(WindowGeometry geometry, IReadOnlyList<PixelRect> screens)
    {
        if (geometry is null)
        {
            throw new ArgumentNullException(nameof(geometry));
        }

        if (screens is null)
        {
            throw new ArgumentNullException(nameof(screens));
        }

        if (geometry.X is not { } x || geometry.Y is not { } y)
        {
            return false;
        }

        var bar = BarOf(geometry, x, y);
        foreach (var screen in screens)
        {
            if (bar.Intersects(screen))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads the place a remembered window opens at, or nothing when it cannot be
    /// seen.
    /// </summary>
    /// <param name="geometry">The remembered window.</param>
    /// <param name="screens">Each screen's working area, in device pixels.</param>
    /// <returns>The position to open at, or null to let the platform centre it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
    public static PixelPoint? PositionOf(WindowGeometry geometry, IReadOnlyList<PixelRect> screens)
    {
        if (!IsReachable(geometry, screens))
        {
            return null;
        }

        return new PixelPoint((int)Math.Round(geometry.X!.Value), (int)Math.Round(geometry.Y!.Value));
    }

    /// <summary>
    /// Builds the strip of the window that has to be on a screen for the window to
    /// be draggable.
    /// </summary>
    /// <param name="geometry">The remembered window.</param>
    /// <param name="x">Its left edge.</param>
    /// <param name="y">Its top edge.</param>
    /// <returns>The title bar's rectangle in device pixels.</returns>
    private static PixelRect BarOf(WindowGeometry geometry, double x, double y)
    {
        var width = Math.Max(1, (int)Math.Round(geometry.Width));
        var height = Math.Max(1, Math.Min(TitleBarHeight, (int)Math.Round(geometry.Height)));
        return new PixelRect((int)Math.Round(x), (int)Math.Round(y), width, height);
    }
}
