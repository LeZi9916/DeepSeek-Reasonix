using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The reference rail-width curve.</summary>
    private static readonly SplineEasing s_railWidthEase = new(0.32, 0.72, 0, 1);

    /// <summary>The reference rail-opacity curve.</summary>
    private static readonly SplineEasing s_railFadeEase = new(0.16, 1, 0.3, 1);

    /// <summary>Identifies the current rail open or close motion.</summary>
    private int _railMotion;

    /// <summary>Whether layout changes should wait for the rail motion to settle.</summary>
    private bool _railMotionActive;

    /// <summary>Starts the reference docked-rail width and opacity motion.</summary>
    /// <param name="opening">Whether the rail is opening.</param>
    /// <returns>Whether the animation took over the layout.</returns>
    private bool StartRailMotion(bool opening)
    {
        if (!Classes.Contains("motion") || LayoutRoot.Bounds.Width <= 640)
        {
            _railMotionActive = false;
            return false;
        }

        _railMotionActive = true;

        var mode = _workspacePreferences.NavigationRail;
        var icons = mode == "on" || (mode == "collapsed" && !opening);
        NavigationRail.IsVisible = icons;
        var iconWidth = icons ? NavigationRail.Width : 0;
        var from = opening ? (Rail.IsVisible ? Rail.Bounds.Width : 0) : Rail.Bounds.Width;
        var fromOpacity = Rail.Opacity;
        var to = opening ? _railWidth : 0;
        if (opening)
        {
            Rail.IsVisible = true;
            Rail.Width = from;
            Rail.Opacity = fromOpacity;
            LayoutRoot.ColumnDefinitions[0].Width = new GridLength(iconWidth + from);
        }

        RailGrip.IsVisible = opening;
        ExpandRail.IsVisible = !opening;
        _ = AnimateRailAsync(++_railMotion, opening, iconWidth, from, to, fromOpacity);
        return true;
    }

    /// <summary>Advances the rail width and opacity at the compositor cadence.</summary>
    /// <param name="generation">The motion this loop owns.</param>
    /// <param name="opening">Whether the rail is opening.</param>
    /// <param name="iconWidth">The icon-rail width in front of the docked rail.</param>
    /// <param name="from">The starting docked width.</param>
    /// <param name="to">The target docked width.</param>
    /// <param name="fromOpacity">The starting rail opacity.</param>
    /// <returns>The completed motion.</returns>
    private async Task AnimateRailAsync(int generation, bool opening, double iconWidth, double from, double to, double fromOpacity)
    {
        var clock = Stopwatch.StartNew();
        while (generation == _railMotion)
        {
            var elapsed = clock.Elapsed.TotalMilliseconds;
            var widthProgress = Math.Clamp(elapsed / 260, 0, 1);
            var fadeProgress = Math.Clamp(elapsed / 180, 0, 1);
            var width = from + ((to - from) * s_railWidthEase.Ease(widthProgress));
            var fade = s_railFadeEase.Ease(fadeProgress);
            Rail.Width = width;
            Rail.Opacity = opening ? fromOpacity + ((1 - fromOpacity) * fade) : fromOpacity * (1 - fade);
            Rail.Margin = new Thickness(iconWidth, 0, 0, 0);
            LayoutRoot.ColumnDefinitions[0].Width = new GridLength(iconWidth + width);
            QuickLinksFrame.Width = Math.Max(0, Math.Min(260, width - 20));
            if (widthProgress >= 1)
            {
                break;
            }

            await Task.Delay(16).ConfigureAwait(true);
        }

        if (generation != _railMotion)
        {
            return;
        }

        Rail.Width = to;
        Rail.Opacity = opening ? 1 : 0;
        LayoutRoot.ColumnDefinitions[0].Width = new GridLength(iconWidth + to);
        _railMotionActive = false;
        LayoutPanels();
    }
}
