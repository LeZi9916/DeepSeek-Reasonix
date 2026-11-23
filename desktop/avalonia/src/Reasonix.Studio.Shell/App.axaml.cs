using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Reasonix.Studio.Shell.Host;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Reasonix.Studio.Shell;

/// <summary>
/// Describes the application object Avalonia creates for Reasonix Studio.
/// </summary>
/// <remarks>
/// The window is built here rather than by a factory elsewhere because the
/// launch owns three facts that outlive it: where the window was on the last
/// close, whether a tray icon should exist, and how a second launch reaches this
/// one. All three are settled before the window is shown.
/// </remarks>
public sealed partial class App : Application
{
    /// <summary>
    /// The tray icon, kept so the window's message port can carry the unread mark.
    /// </summary>
    private TrayIcon? _tray;

    /// <summary>
    /// Gets or sets the activation channel the process entry point created.
    /// </summary>
    /// <remarks>
    /// Set for the first process only: a later launch raises that process and
    /// exits, so it never reaches the interface at all.
    /// </remarks>
    public static InstanceActivation? Activation
    {
        get; set;
    }

    /// <summary>
    /// Loads the XAML that declares this application's styles.
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Creates the main window once the desktop lifetime has started.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = CreateWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Moves the window to its remembered place when that place is still visible.
    /// </summary>
    /// <param name="window">The window to place.</param>
    /// <param name="geometry">The remembered geometry.</param>
    /// <remarks>
    /// A place no screen covers is dropped rather than clamped: the platform then
    /// centres the window, which is where it opens on a first run anyway.
    /// </remarks>
    private static void Place(MainWindow window, WindowGeometry geometry)
    {
        var screens = new List<PixelRect>();
        foreach (var screen in window.Screens.All)
        {
            screens.Add(screen.WorkingArea);
        }

        if (WindowPlacement.PositionOf(geometry, screens) is not { } place)
        {
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = place;
    }

    /// <summary>
    /// Reads the window's current place for the next launch.
    /// </summary>
    /// <param name="window">The window being closed.</param>
    /// <returns>The geometry, with position left out while maximised.</returns>
    /// <remarks>
    /// A maximised window's position is the maximised frame's, and restoring that
    /// as a normal window's place would open it in the corner it covered. The
    /// restored place is what the person chose, so it is not overwritten.
    /// </remarks>
    private static WindowGeometry GeometryOf(MainWindow window)
    {
        var maximised = window.WindowState == WindowState.Maximized;
        var bounds = maximised
            ? new WindowGeometry(null, null, window.Width, window.Height, Maximised: true)
            : new WindowGeometry(window.Position.X, window.Position.Y, window.Width, window.Height, Maximised: false);
        return bounds with
        {
            RailWidth = window.RailWidth,
            ReducedEffects = window.ReducedEffects,
            QueueDuringRun = window.QueueDuringRun,
            NewTaskPreset = window.NewTaskPreset,
            HideLegacySessions = window.HideLegacySessions,
            Theme = window.RequestedThemeVariant?.Key as string,
        };
    }

    /// <summary>
    /// Brings the window back in front of whatever the person was doing.
    /// </summary>
    /// <param name="window">The window to raise.</param>
    private static void Raise(MainWindow window)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    /// <summary>
    /// Names the file the window's geometry is kept in.
    /// </summary>
    /// <returns>The path, under the shell's own state directory.</returns>
    private static string GeometryPath()
    {
        return Path.Combine(ShellLog.Directory(), "window.json");
    }

    /// <summary>
    /// Spells one geometry for the log.
    /// </summary>
    /// <param name="geometry">The geometry.</param>
    /// <returns>The numbers, so a support report can say where it opened.</returns>
    private static string Describe(WindowGeometry geometry)
    {
        var place = geometry.X is { } x && geometry.Y is { } y
            ? string.Create(CultureInfo.InvariantCulture, $"at {x},{y}")
            : "centred";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{place} {geometry.Width}x{geometry.Height}{(geometry.Maximised ? " maximised" : string.Empty)}");
    }

    /// <summary>
    /// Builds the window, restores its place and gives it a tray icon.
    /// </summary>
    /// <returns>The window the shell runs in.</returns>
    private MainWindow CreateWindow()
    {
        var window = new MainWindow();
        window.ConfigureNativeTitleBar();
        ConfigureGlobalShortcut(window);
        var store = new WindowStateStore(GeometryPath());
        var geometry = store.Load();
        window.QueueDuringRun = geometry.QueueDuringRun;
        window.NewTaskPreset = geometry.NewTaskPreset;
        window.HideLegacySessions = geometry.HideLegacySessions;
        window.SaveWindowPreferences = () => store.Save(GeometryOf(window));
        window.RestoreChrome(geometry.RailWidth, geometry.Theme, geometry.ReducedEffects);

        // Size now, position when the platform can answer which screens exist.
        window.Width = geometry.Width;
        window.Height = geometry.Height;
        if (geometry.Maximised)
        {
            window.WindowState = WindowState.Maximized;
        }

        window.Opened += (_, _) => Place(window, geometry);
        window.Closed += (_, _) =>
        {
            var closing = GeometryOf(window);
            store.Save(closing);
            ShellLog.Line("window closed " + Describe(closing));
        };

        _tray = CreateTray(window);
        window.Icon = TrayGlyph.Icon();

        if (Activation is not null)
        {
            Activation.Activated += () => Dispatcher.UIThread.Post(() => Raise(window));
        }

        return window;
    }

    /// <summary>
    /// Creates the tray icon, or nothing where the platform has no tray.
    /// </summary>
    /// <param name="window">The window the icon raises.</param>
    /// <returns>The icon, or null when it could not be created.</returns>
    /// <remarks>
    /// A tray icon is a convenience, never a requirement: an icon that could not
    /// be created leaves the window exactly as usable as it was.
    /// </remarks>
    private TrayIcon? CreateTray(MainWindow window)
    {
        try
        {
            var tray = new TrayIcon
            {
                Icon = TrayGlyph.Icon(),
                ToolTipText = "Reasonix Studio",
                IsVisible = true,
            };

            window.AttachTray(tray, () => Raise(window));
            tray.Clicked += (_, _) => Raise(window);

            var icons = new TrayIcons();
            icons.Add(tray);
            TrayIcon.SetIcons(this, icons);
            return tray;
        }
        catch (Exception error)
        {
            window.DetachTray();
            ShellLog.Line("tray unavailable: " + error.Message);
            return null;
        }
    }
}
