using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Host;

/// <summary>Selects files without invoking their registered application.</summary>
internal static class NativeFileSelection
{
    /// <summary>Uses the platform selection API, or opens the containing directory.</summary>
    /// <param name="file">The existing entry.</param><param name="openFolder">The platform directory launcher.</param>
    /// <returns>Whether the platform accepted the request.</returns>
    internal static async Task<bool> SelectAsync(FileInfo file, Func<DirectoryInfo, Task<bool>> openFolder)
    {
        if (OperatingSystem.IsWindows())
        {
            return SelectWindows(file.FullName);
        }

        if (OperatingSystem.IsMacOS())
        {
            var start = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
            start.ArgumentList.Add("-R");
            start.ArgumentList.Add(file.FullName);
            using var process = Process.Start(start);
            if (process is null)
            {
                return false;
            }

            await process.WaitForExitAsync().ConfigureAwait(true);
            return process.ExitCode == 0;
        }

        return file.Directory is { } parent && await openFolder(parent).ConfigureAwait(true);
    }

    /// <summary>Passes the full item identifier to the Windows shell selection API.</summary>
    /// <param name="path">The existing file path.</param><returns>The shell acknowledgment.</returns>
    [SupportedOSPlatform("windows")]
    private static bool SelectWindows(string path)
    {
        var initialized = InitializeCom(nint.Zero, 2);
        var item = CreateItem(path);
        try
        {
            return item != nint.Zero && SelectItem(item, 0, nint.Zero, 0) >= 0;
        }
        finally
        {
            if (item != nint.Zero)
            {
                FreeItem(item);
            }

            if (initialized >= 0)
            {
                UninitializeCom();
            }
        }
    }

    /// <summary>Initializes apartment services for this shell call.</summary>
    /// <param name="reserved">Reserved null pointer.</param><param name="mode">Apartment mode.</param><returns>The COM result.</returns>
    [DllImport("ole32.dll", EntryPoint = "CoInitializeEx")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int InitializeCom(nint reserved, uint mode);

    /// <summary>Balances a successful COM initialization.</summary>
    [DllImport("ole32.dll", EntryPoint = "CoUninitialize")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void UninitializeCom();

    /// <summary>Resolves the full filesystem item identifier.</summary>
    /// <param name="path">The full path.</param><returns>The allocated identifier.</returns>
    [DllImport("shell32.dll", EntryPoint = "ILCreateFromPathW", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint CreateItem(string path);

    /// <summary>Selects the full item when the child count is zero.</summary>
    /// <param name="folder">The full item identifier.</param><param name="count">Zero for a full item.</param>
    /// <param name="children">Unused null pointer.</param><param name="flags">Shell flags.</param><returns>The shell result.</returns>
    [DllImport("shell32.dll", EntryPoint = "SHOpenFolderAndSelectItems")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int SelectItem(nint folder, uint count, nint children, uint flags);

    /// <summary>Releases a shell-allocated item identifier.</summary>
    /// <param name="item">The allocated identifier.</param>
    [DllImport("shell32.dll", EntryPoint = "ILFree")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void FreeItem(nint item);
}
