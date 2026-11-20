using Avalonia.Platform.Storage;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Host;

/// <content>
/// The path half of the host: showing and opening entries the kernel named.
/// </content>
/// <remarks>
/// A pane's base is a workspace the kernel resolved; the shell only shows an entry
/// inside it. Keeping that rule in one place rather than at each verb is what stops
/// a later call site from being the one that forgot it.
/// </remarks>
public sealed partial class AvaloniaHost
{
    /// <summary>
    /// Reports whether a path stays inside a workspace base.
    /// </summary>
    /// <param name="root">The workspace's root.</param>
    /// <param name="candidate">The resolved path to test.</param>
    /// <returns>True when the path is the root itself or something under it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="candidate"/> is null.</exception>
    public static bool IsInside(string root, string candidate)
    {
        if (candidate is null)
        {
            throw new ArgumentNullException(nameof(candidate));
        }

        if (string.IsNullOrEmpty(root))
        {
            return false;
        }

        var comparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var resolvedRoot = TrimSeparators(Path.GetFullPath(root));
        var resolvedCandidate = TrimSeparators(candidate);

        if (string.Equals(resolvedCandidate, resolvedRoot, comparer))
        {
            return true;
        }

        // The separator is part of the test: without it a sibling named
        // "workspace-old" would pass as being inside "workspace".
        return resolvedCandidate.StartsWith(
            resolvedRoot + Path.DirectorySeparatorChar,
            comparer);
    }

    /// <inheritdoc/>
    public Task<Refusal?> RevealPathAsync(string baseAddress, string path)
    {
        return InWorkspace(baseAddress, path, "this shell cannot show files", ShowInFileManagerAsync);
    }

    /// <inheritdoc/>
    public Task<Refusal?> OpenFileAsync(string baseAddress, string path)
    {
        return InWorkspace(baseAddress, path, "this shell cannot open files", OpenWithDefaultAsync);
    }

    /// <inheritdoc/>
    public Task<Refusal?> RevealWorkspaceAsync(string root)
    {
        return LauncherOf() is null
            ? Task.FromResult<Refusal?>(new Refusal(null, "this shell cannot show files"))
            : ShowInFileManagerAsync(root);
    }

    /// <inheritdoc/>
    public Task<Refusal?> RevealLogsAsync()
    {
        return LauncherOf() is null
            ? Task.FromResult<Refusal?>(new Refusal(null, "this shell cannot show files"))
            : ShowInFileManagerAsync(ShellLog.Directory());
    }

    /// <inheritdoc/>
    public Task<Refusal?> OpenWorkspaceAsync(string root)
    {
        return LauncherOf() is null
            ? Task.FromResult<Refusal?>(new Refusal(null, "this shell cannot open folders"))
            : ShowInFileManagerAsync(root);
    }

    /// <summary>
    /// Drops a trailing separator so two spellings of one directory compare equal.
    /// </summary>
    /// <param name="value">The path to trim.</param>
    /// <returns>The path without a trailing separator.</returns>
    private static string TrimSeparators(string value)
    {
        return value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// Resolves an entry against its workspace base and runs one verb on it.
    /// </summary>
    /// <param name="baseAddress">The pane's workspace base.</param>
    /// <param name="path">The entry, relative to that root.</param>
    /// <param name="unsupported">What to say when this shell has no launcher.</param>
    /// <param name="verb">The action to run on the resolved path.</param>
    /// <returns>Null once handled, otherwise why it was not.</returns>
    private Task<Refusal?> InWorkspace(
        string baseAddress,
        string path,
        string unsupported,
        Func<string, Task<Refusal?>> verb)
    {
        if (LauncherOf() is null)
        {
            return Task.FromResult<Refusal?>(new Refusal(null, unsupported));
        }

        // A path that escapes the base is refused here rather than resolved against
        // whatever the process happens to be running from.
        var combined = Path.GetFullPath(Path.Combine(baseAddress, path));
        if (!IsInside(baseAddress, combined))
        {
            return Task.FromResult<Refusal?>(
                new Refusal("shell.path.outside", "that entry is outside the workspace"));
        }

        return verb(combined);
    }

    /// <summary>
    /// Shows a path in the platform's file manager.
    /// </summary>
    /// <param name="path">The path to show.</param>
    /// <returns>Null once shown, otherwise why it was not.</returns>
    private Task<Refusal?> ShowInFileManagerAsync(string path)
    {
        var launcher = LauncherOf();
        if (launcher is null)
        {
            return Task.FromResult<Refusal?>(new Refusal(null, "this shell cannot show files"));
        }

        try
        {
            if (Directory.Exists(path))
            {
                _ = launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
                return Task.FromResult<Refusal?>(null);
            }

            if (File.Exists(path))
            {
                // The platform's file manager selects the entry when handed the
                // file itself, which is what "reveal" means to the person asking.
                _ = launcher.LaunchFileInfoAsync(new FileInfo(path));
                return Task.FromResult<Refusal?>(null);
            }

            return Task.FromResult<Refusal?>(
                new Refusal("shell.path.missing", "that path is no longer there"));
        }
        catch (Exception error)
        {
            return Task.FromResult<Refusal?>(new Refusal("shell.launch.failed", error.Message));
        }
    }

    /// <summary>
    /// Opens a path with whatever the platform registered for it.
    /// </summary>
    /// <param name="path">The path to open.</param>
    /// <returns>Null once opened, otherwise why it was not.</returns>
    private Task<Refusal?> OpenWithDefaultAsync(string path)
    {
        var launcher = LauncherOf();
        if (launcher is null)
        {
            return Task.FromResult<Refusal?>(new Refusal(null, "this shell cannot open files"));
        }

        try
        {
            _ = launcher.LaunchFileInfoAsync(new FileInfo(path));
            return Task.FromResult<Refusal?>(null);
        }
        catch (Exception error)
        {
            return Task.FromResult<Refusal?>(new Refusal("shell.launch.failed", error.Message));
        }
    }

    /// <summary>
    /// Reads the window's launcher.
    /// </summary>
    /// <returns>The launcher, or null when there is no window to ask.</returns>
    private ILauncher? LauncherOf()
    {
        return _window?.Launcher;
    }
}
