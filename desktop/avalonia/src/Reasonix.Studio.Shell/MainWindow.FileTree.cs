using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Shell.Workbench;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FileEntry = Reasonix.Studio.Shell.Workbench.WorkspaceFileEntry;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Directory snapshots belonging to the current workspace.</summary>
    private readonly Dictionary<string, WorkspaceListing> _fileDirectories = new(StringComparer.Ordinal);

    /// <summary>Folders the reader explicitly collapsed.</summary>
    private readonly HashSet<string> _fileCollapsed = new(StringComparer.Ordinal);

    /// <summary>Outstanding folder reads, independent of file-editor reads.</summary>
    private readonly Dictionary<string, object> _fileFolderReads = new(StringComparer.Ordinal);

    /// <summary>Current search projection and its independently folded folders.</summary>
    private (WorkspaceListing Listing, HashSet<string> Closed)? _fileSearch;

    /// <summary>Invalidates listing responses across reloads and workspace changes.</summary>
    private long _fileListingRead;

    /// <summary>Retires one workspace's directory state.</summary>
    private void ResetFileTree()
    {
        _fileListingRead++;
        _fileDirectories.Clear();
        _fileCollapsed.Clear();
        _fileFolderReads.Clear();
        _fileSearch = null;
    }

    /// <summary>Reloads the root and still-reachable expanded folders without changing the reader's place.</summary>
    /// <returns>The directory snapshot.</returns>
    private async Task ListFilesAsync()
    {
        if (_engine is not { } engine)
        {
            return;
        }

        var ticket = ++_fileListingRead;
        _fileFolderReads.Clear();
        var query = WorkbenchSearch.Text?.Trim() ?? string.Empty;
        var open = _fileDirectories.Keys.Where(p => p.Length > 0 && !_fileCollapsed.Contains(p)).OrderBy(p => p.Count(c => c == '/')).ToArray();
        try
        {
            var root = await engine.Pages.FilesAsync(string.Empty, query, _showHiddenFiles, _closing.Token).ConfigureAwait(true);
            var snapshots = new Dictionary<string, WorkspaceListing>(StringComparer.Ordinal) { [string.Empty] = root };
            if (query.Length == 0)
            {
                foreach (var path in open)
                {
                    if (!snapshots.Values.Any(list => list.Directories.Contains(path, StringComparer.Ordinal)))
                    {
                        continue;
                    }

                    try
                    {
                        snapshots[path] = await engine.Pages.FilesAsync(path, string.Empty, _showHiddenFiles, _closing.Token).ConfigureAwait(true);
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                    }
                }
            }

            if (ticket != _fileListingRead || _closing.IsCancellationRequested)
            {
                return;
            }

            if (query.Length > 0)
            {
                _fileSearch = (root, new HashSet<string>(StringComparer.Ordinal));
            }
            else
            {
                _fileSearch = null;
                _fileDirectories.Clear();
                _fileCollapsed.Clear();
                foreach (var (path, listing) in snapshots)
                {
                    _fileDirectories[path] = listing;
                    _fileCollapsed.UnionWith(listing.Directories.Where(dir => !snapshots.ContainsKey(dir)));
                }
            }

            DrawFileTree();
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (ticket == _fileListingRead)
            {
                ReportWorkbenchError(error);
            }
        }
    }

    /// <summary>Expands a directory in place while retaining sibling paths and the open editor.</summary>
    /// <param name="path">Workspace-relative directory.</param>
    /// <returns>The optional child listing.</returns>
    private async Task ToggleFileFolderAsync(string path)
    {
        if (_fileSearch is { } search)
        {
            if (!search.Closed.Add(path))
            {
                search.Closed.Remove(path);
            }

            DrawFileTree();
            return;
        }

        if (_fileFolderReads.Remove(path) || !_fileCollapsed.Contains(path))
        {
            _fileCollapsed.Add(path);
            DrawFileTree();
            return;
        }

        if (_engine is not { } engine)
        {
            return;
        }

        var ticket = new object();
        var generation = _fileListingRead;
        _fileFolderReads[path] = ticket;
        try
        {
            var listing = await engine.Pages.FilesAsync(path, string.Empty, _closing.Token).ConfigureAwait(true);
            if (generation != _fileListingRead || !_fileFolderReads.TryGetValue(path, out var pending) || !ReferenceEquals(ticket, pending) || _closing.IsCancellationRequested)
            {
                return;
            }

            var known = _fileDirectories.Values.SelectMany(list => list.Directories).ToHashSet(StringComparer.Ordinal);
            _fileDirectories[path] = listing;
            _fileCollapsed.Remove(path);
            _fileCollapsed.UnionWith(listing.Directories.Where(dir => !known.Contains(dir)));
            DrawFileTree();
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (generation == _fileListingRead && _fileFolderReads.TryGetValue(path, out var pending) && ReferenceEquals(ticket, pending))
            {
                ReportWorkbenchError(error);
            }
        }
        finally
        {
            if (_fileFolderReads.TryGetValue(path, out var pending) && ReferenceEquals(ticket, pending))
            {
                _fileFolderReads.Remove(path);
            }
        }
    }

    /// <summary>Projects directory snapshots into the reference's folders-first tree order.</summary>
    private void DrawFileTree()
    {
        var listings = _fileSearch is { } search ? [search.Listing] : _fileDirectories.Values.ToArray();
        var closed = _fileSearch?.Closed ?? _fileCollapsed;
        var files = listings.SelectMany(list => list.Files).ToHashSet(StringComparer.Ordinal);
        var folders = listings.SelectMany(list => list.Directories).ToHashSet(StringComparer.Ordinal);
        foreach (var path in files.ToArray())
        {
            for (var cut = path.LastIndexOf('/'); cut > 0; cut = path.LastIndexOf('/', cut - 1))
            {
                folders.Add(path[..cut]);
            }
        }

        var statuses = GitRepositories.Children.OfType<GitRepositoryView>().OrderBy(repo => repo.Directory.Length).SelectMany(repo => repo.FileStatuses.Select(row =>
            (Path: repo.Directory.Length == 0 ? row.Path : repo.Directory + "/" + row.Path, row.Status))).GroupBy(row => row.Path, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Last().Status, StringComparer.Ordinal);
        var rows = new List<FileEntry>();
        Walk(string.Empty);
        WorkspaceFiles.SelectionChanged -= OnFileSelected;
        WorkspaceFiles.ItemsSource = rows;
        WorkspaceFiles.SelectedItem = rows.FirstOrDefault(row => !row.Directory && row.Path == _openFile?.Original.Path);
        WorkspaceFiles.SelectionChanged += OnFileSelected;
        NoWorkspaceFiles.IsVisible = WorkspaceFiles.IsVisible && rows.Count == 0;

        void Walk(string parent)
        {
            foreach (var path in folders.Where(path => Parent(path) == parent).Order(StringComparer.CurrentCulture))
            {
                rows.Add(new FileEntry(path, true) { Expanded = !closed.Contains(path) });
                if (!closed.Contains(path))
                {
                    Walk(path);
                }
            }

            rows.AddRange(files.Where(path => Parent(path) == parent).Order(StringComparer.CurrentCulture).Select(path => new FileEntry(path, false) { Status = statuses.GetValueOrDefault(path, string.Empty) }));
        }

        static string Parent(string path) => path.LastIndexOf('/') is var cut && cut >= 0 ? path[..cut] : string.Empty;
    }

    /// <summary>Supports disclosure and parent navigation when focus is on the selected tree item.</summary>
    /// <param name="sender">Explorer list.</param>
    /// <param name="e">Navigation key.</param>
    private async void OnFileTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (WorkspaceFiles.SelectedItem is not FileEntry entry)
        {
            return;
        }

        if (entry.Directory && (e.Key == Key.Enter || (e.Key == Key.Right && !entry.Expanded) || (e.Key == Key.Left && entry.Expanded)))
        {
            e.Handled = true;
            await ToggleFileFolderAsync(entry.Path).ConfigureAwait(true);
            WorkspaceFiles.SelectedItem = WorkspaceFiles.Items.OfType<FileEntry>().FirstOrDefault(row => row.Directory && row.Path == entry.Path);
        }
        else if (e.Key == Key.Left && entry.Path.LastIndexOf('/') is var cut && cut > 0)
        {
            e.Handled = true;
            WorkspaceFiles.SelectedItem = WorkspaceFiles.Items.OfType<FileEntry>().FirstOrDefault(row => row.Directory && row.Path == entry.Path[..cut]);
        }
    }

    /// <summary>Handles pointer and keyboard activation of the row's native button.</summary>
    /// <param name="sender">File list.</param>
    /// <param name="e">Row activation.</param>
    private async void OnWorkspaceFileClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button { DataContext: FileEntry entry })
        {
            if (entry.Directory)
            {
                await ToggleFileFolderAsync(entry.Path).ConfigureAwait(true);
            }
            else if (Equals(WorkspaceFiles.SelectedItem, entry))
            {
                await OpenEntryAsync(entry).ConfigureAwait(true);
            }
            else
            {
                WorkspaceFiles.SelectedItem = entry;
            }
        }
    }

    /// <summary>Opens the keyboard-selected file without treating a folder as a navigation root.</summary>
    /// <param name="sender">Explorer list.</param>
    /// <param name="e">Selection change.</param>
    private async void OnFileSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_engine is not null && WorkspaceFiles.SelectedItem is FileEntry { Directory: false } entry)
        {
            await OpenEntryAsync(entry).ConfigureAwait(true);
        }
    }

    /// <summary>Reflects the active canvas file without issuing another file read.</summary>
    /// <param name="path">Workspace-relative path, or no file for a Git canvas.</param>
    private void SelectWorkspaceFile(string? path)
    {
        WorkspaceFiles.SelectionChanged -= OnFileSelected;
        WorkspaceFiles.SelectedItem = WorkspaceFiles.Items.OfType<FileEntry>().FirstOrDefault(row => !row.Directory && row.Path == path);
        WorkspaceFiles.SelectionChanged += OnFileSelected;
    }
}
