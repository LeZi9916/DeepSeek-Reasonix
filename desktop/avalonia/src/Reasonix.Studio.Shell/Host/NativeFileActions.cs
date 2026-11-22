using System;
using System.IO;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Host;

/// <summary>Platform acknowledgments for opening folders, opening files and selecting entries.</summary>
/// <param name="OpenFolder">Opens a directory.</param><param name="OpenFile">Opens the registered application.</param>
/// <param name="SelectFile">Selects an entry in its containing directory.</param>
internal sealed record NativeFileActions(
    Func<DirectoryInfo, Task<bool>> OpenFolder,
    Func<FileInfo, Task<bool>> OpenFile,
    Func<FileInfo, Task<bool>> SelectFile);
