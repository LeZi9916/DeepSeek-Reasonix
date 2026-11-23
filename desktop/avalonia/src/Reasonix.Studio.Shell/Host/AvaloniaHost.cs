using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Host;

/// <summary>
/// The host port for a native Avalonia window.
/// </summary>
/// <remarks>
/// Every verb is answered from Avalonia's own platform surface rather than from a
/// second implementation of it, so the shell inherits whatever the platform's file
/// manager, dialogs and launcher already do. A shell with no window answers the
/// unsupported value rather than throwing: a page that asks early is not an error.
/// The path and launcher half lives in a file of its own, because showing an entry
/// in a file manager is a different concern from owning a window.
/// </remarks>
public sealed partial class AvaloniaHost : IHostPort
{
    /// <summary>
    /// The window the verbs act on, or null before one exists.
    /// </summary>
    private readonly Window? _window;

    /// <summary>
    /// Initializes a new instance of the <see cref="AvaloniaHost"/> class.
    /// </summary>
    /// <param name="window">The window the verbs act on, or null before one exists.</param>
    /// <param name="platform">The platform name the page should use, or empty to read it from the runtime.</param>
    public AvaloniaHost(Window? window, string? platform = null)
    {
        _window = window;
        Platform = platform ?? PlatformName();
    }

    /// <summary>
    /// Gets the platform spelled the way the page spells it.
    /// </summary>
    public string Platform
    {
        get;
    }

    /// <summary>
    /// Gets or sets the tray icon whose tooltip carries the unread mark.
    /// </summary>
    /// <remarks>
    /// Set by whoever created the icon; a window with no tray shows the mark
    /// nowhere, which is what a platform without one can offer.
    /// </remarks>
    public TrayIcon? Tray
    {
        get; set;
    }

    /// <inheritdoc/>
    public bool InShell
    {
        get => true;
    }

    /// <inheritdoc/>
    public bool CapturesWindow
    {
        get => _window is not null;
    }

    /// <inheritdoc/>
    public bool RevealsFiles
    {
        get => LauncherOf() is not null;
    }

    /// <inheritdoc/>
    public bool OpensFiles
    {
        get => LauncherOf() is not null;
    }

    /// <inheritdoc/>
    public bool RevealsLogs
    {
        get => LauncherOf() is not null;
    }

    /// <inheritdoc/>
    public bool OpensFolders
    {
        get => LauncherOf() is not null;
    }

    /// <summary>
    /// Reads the platform name the page spells, from the runtime's own name.
    /// </summary>
    /// <returns>"windows", "darwin" or "linux".</returns>
    public static string PlatformName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }

        return OperatingSystem.IsMacOS() ? "darwin" : "linux";
    }

    /// <inheritdoc/>
    public HostInfo Describe()
    {
        // This window is created without the native title bar, which is what lets
        // the page's own first row be the chrome. Only the shell knows how its
        // window was created, so the page asks rather than guessing.
        return new HostInfo(ShellKind.Native, Platform, TitleBar: true);
    }

    /// <inheritdoc/>
    public void MinimiseWindow()
    {
        if (_window is not null)
        {
            _window.WindowState = WindowState.Minimized;
        }
    }

    /// <inheritdoc/>
    public void ToggleMaximiseWindow()
    {
        if (_window is null)
        {
            return;
        }

        _window.WindowState = _window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    /// <inheritdoc/>
    public void CloseWindow()
    {
        _window?.Close();
    }

    /// <inheritdoc/>
    public Task<bool> IsWindowMaximisedAsync()
    {
        return Task.FromResult(_window?.WindowState == WindowState.Maximized);
    }

    /// <inheritdoc/>
    public void SyncTheme(string mode, bool dark)
    {
        if (_window is null)
        {
            return;
        }

        // The window's variant is what the platform reads for its chrome, so the
        // page's resolved scheme is pushed here rather than read back from the
        // system: a reader who chose light on a dark desktop sees light chrome.
        _window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    /// <inheritdoc/>
    public Task<byte[]?> CaptureWindowAsync()
    {
        if (_window is null || _window.Bounds.Width < 1 || _window.Bounds.Height < 1)
        {
            return Task.FromResult<byte[]?>(null);
        }

        try
        {
            var size = new PixelSize(
                Math.Max(1, (int)_window.Bounds.Width),
                Math.Max(1, (int)_window.Bounds.Height));
            using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
            bitmap.Render(_window);

            using var stream = new MemoryStream();
            bitmap.Save(stream, PngBitmapEncoderOptions.Default);
            return Task.FromResult<byte[]?>(stream.ToArray());
        }
        catch (Exception)
        {
            // A window that cannot be rendered right now is not a failure the
            // caller can act on; it asked for a picture and gets none.
            return Task.FromResult<byte[]?>(null);
        }
    }

    /// <inheritdoc/>
    public Task<GraphicsInfo?> GraphicsAsync()
    {
        // What draws this window is the platform implementation Avalonia created
        // for it. There is no public renderer handle to name, and a guess would be
        // worse than the name of the thing that actually holds the surface.
        var compositing = _window?.PlatformImpl?.GetType().Name ?? string.Empty;
        return Task.FromResult<GraphicsInfo?>(new GraphicsInfo(LaunchedOff: false, SavedOff: null, compositing));
    }

    /// <inheritdoc/>
    public Task<GlobalShortcutInfo> GlobalShortcutAsync()
    {
        return Task.FromResult(_window is MainWindow { WindowShortcut: { } shortcut }
            ? new GlobalShortcutInfo(shortcut.Binding, shortcut.Status)
            : GlobalShortcutInfo.Unsupported);
    }

    /// <inheritdoc/>
    public Task<GlobalShortcutInfo> SetGlobalShortcutAsync(string binding)
    {
        if (_window is not MainWindow { WindowShortcut: { } shortcut })
        {
            return Task.FromResult(GlobalShortcutInfo.Unsupported);
        }

        return Dispatcher.UIThread.CheckAccess() ? Task.FromResult(Apply()) : Dispatcher.UIThread.InvokeAsync(Apply).GetTask();

        GlobalShortcutInfo Apply()
        {
            var outcome = shortcut.Apply(binding);
            return new GlobalShortcutInfo(shortcut.Binding, shortcut.Status)
            {
                Error = outcome is GlobalShortcutStatus.Disabled or GlobalShortcutStatus.Registered ? null : outcome,
            };
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ProcessMemory>> ProcessMemoryAsync()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            IReadOnlyList<ProcessMemory> rows = new[]
            {
                new ProcessMemory("main", process.PrivateMemorySize64, process.WorkingSet64),
            };
            return Task.FromResult(rows);
        }
        catch (Exception)
        {
            // A process that cannot be inspected reports nothing rather than
            // failing the report that asked.
            return Task.FromResult<IReadOnlyList<ProcessMemory>>(Array.Empty<ProcessMemory>());
        }
    }

    /// <inheritdoc/>
    public async Task OpenExternalAsync(string url)
    {
        var launcher = LauncherOf();
        if (launcher is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return;
        }

        // Only the two schemes a page may open: a shell that launched any scheme
        // would be a way to run a program by writing a link.
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }

        await launcher.LaunchUriAsync(uri).ConfigureAwait(true);
    }

    /// <inheritdoc/>
    public Task SetUnreadBadgeAsync(bool unread)
    {
        var tray = Tray;
        if (tray is not null)
        {
            tray.ToolTipText = unread ? "Reasonix Studio — unread" : "Reasonix Studio";
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> PathsForFiles(IReadOnlyList<string> paths)
    {
        // A drop on this window already carries paths: Avalonia hands the platform
        // item's own path, so nothing has to be looked up again.
        return paths ?? Array.Empty<string>();
    }

    /// <inheritdoc/>
    public async Task<string?> SaveTextAsync(string name, string content)
    {
        var file = await SaveFileAsync(name).ConfigureAwait(true);
        if (file is null)
        {
            return null;
        }

        var path = file.TryGetLocalPath();
        if (path is null)
        {
            return string.Empty;
        }

        await File.WriteAllTextAsync(path, content).ConfigureAwait(true);
        return path;
    }

    /// <inheritdoc/>
    public async Task<string?> SaveBytesAsync(string name, ReadOnlyMemory<byte> bytes)
    {
        var file = await SaveFileAsync(name).ConfigureAwait(true);
        if (file is null)
        {
            return null;
        }

        var path = file.TryGetLocalPath();
        if (path is null)
        {
            return string.Empty;
        }

        await File.WriteAllBytesAsync(path, bytes.ToArray()).ConfigureAwait(true);
        return path;
    }

    /// <inheritdoc/>
    public Task<string?> ShowTextContextMenuAsync(TextContextMenuRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        // The platform's own menu belongs to whatever control holds the selection;
        // a shell-level menu could undo inside a field it does not own, which is
        // worse than no menu at all. The controls that own a selection draw theirs.
        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc/>
    public async Task<string?> PickFolderAsync(string startIn)
    {
        var storage = StorageOf();
        if (storage is null)
        {
            return null;
        }

        IStorageFolder? start = null;
        if (!string.IsNullOrEmpty(startIn))
        {
            start = await storage.TryGetFolderFromPathAsync(startIn).ConfigureAwait(true);
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            SuggestedStartLocation = start,
        }).ConfigureAwait(true);

        var chosen = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        return chosen ?? string.Empty;
    }

    /// <summary>
    /// Opens the save dialog for one file.
    /// </summary>
    /// <param name="name">The name to offer.</param>
    /// <returns>
    /// The file the person picked, or null when they dismissed the dialog or there
    /// is no picker.
    /// </returns>
    private async Task<IStorageFile?> SaveFileAsync(string name)
    {
        var storage = StorageOf();
        if (storage is null)
        {
            return null;
        }

        return await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = name,
            ShowOverwritePrompt = true,
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Reads the window's storage provider.
    /// </summary>
    /// <returns>The provider, or null when there is no window to ask.</returns>
    private IStorageProvider? StorageOf()
    {
        return _window?.StorageProvider;
    }
}
