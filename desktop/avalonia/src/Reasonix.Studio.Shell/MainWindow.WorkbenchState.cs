using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.Shell.Session;
using Reasonix.Studio.Shell.Workbench;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Retained visual state for each independently running conversation.</summary>
    private readonly Dictionary<SessionEngine, WorkbenchPresentation> _workbenchStates = [];

    /// <summary>Stable editor namespaces owned by each retained runtime.</summary>
    private readonly Dictionary<SessionEngine, string> _workbenchKeys = [];

    /// <summary>The active buffer namespace, including remote host and runtime.</summary>
    private string _workspaceBufferScope = string.Empty;

    /// <summary>The runtime whose workbench is mounted.</summary>
    private SessionEngine? _boundWorkspaceEngine;

    /// <summary>Gets the active runtime's stable editor-buffer namespace.</summary>
    private string WorkspaceBufferScope => _workspaceBufferScope.Length > 0 ? _workspaceBufferScope : _workspaceRoot;

    /// <summary>Separates identical paths on different machines and in different runtimes.</summary>
    /// <param name="root">Canonical workspace.</param>
    /// <returns>The editor buffer identity.</returns>
    private string BufferScopeFor(string root)
    {
        if (_engine is not { } engine)
        {
            return root;
        }

        if (!_workbenchKeys.ContainsKey(engine))
        {
            _workbenchKeys[engine] = Guid.NewGuid().ToString("N");
        }

        return JsonSerializer.Serialize(new[] { engine.RemoteHost, root, _workbenchKeys[engine] });
    }

    /// <summary>Retains widgets without cancelling a background runtime's work.</summary>
    private void CaptureWorkbenchPresentation()
    {
        if (_boundWorkspaceEngine is not { } engine)
        {
            return;
        }

        _workbenchStates[engine] = new WorkbenchPresentation
        {
            Root = _workspaceRoot,
            Open = WorkbenchPanel.IsVisible,
            Expanded = _fullWorkbench,
            Hidden = _showHiddenFiles,
            File = _openFile?.Original.Path,
            Source = FileEditor.IsVisible && !WorkbenchStart.IsVisible,
            Diff = DiffFile.Classes.Contains("active"),
            Launcher = WorkbenchStart.IsVisible,
            Browser = ManualBrowserHost.Content as Control,
            Browsers = _browserPages.ToArray(),
            BrowserVisible = ManualBrowserHost.IsVisible,
            Terminal = _terminalId,
            Folders = new Dictionary<string, WorkspaceListing>(_fileDirectories, StringComparer.Ordinal),
            Collapsed = new HashSet<string>(_fileCollapsed, StringComparer.Ordinal),
            Explorer = ExplorerPane.IsVisible,
            ExplorerOffset = ExplorerScroller.Offset,
            Caret = FileEditor.CaretIndex,
            Git = _gitSurface,
            GitApi = _gitApi,
            GitLifetime = _gitLifetime,
            Repositories = GitRepositories.Children.OfType<GitRepositoryView>().ToArray(),
        };
        _gitLifetime = null;
        _gitApi = null;
        _gitSurface = null;
        GitRepositories.Children.Clear();
    }

    /// <summary>Mounts retained workspace state before fresh reads can update its widgets.</summary>
    private void RestoreWorkbenchPresentation()
    {
        _browserPages.Clear();
        _showHiddenFiles = false;
        _fullWorkbench = false;
        if (_engine is not { } engine || !_workbenchStates.TryGetValue(engine, out var state) || state.Root != _workspaceRoot)
        {
            return;
        }

        _showHiddenFiles = state.Hidden;
        _fullWorkbench = state.Expanded;
        HiddenFiles.Classes.Set("active", state.Hidden);
        HiddenSlash.IsVisible = !state.Hidden;
        foreach (var (path, listing) in state.Folders)
        {
            _fileDirectories[path] = listing;
        }

        _fileCollapsed.UnionWith(state.Collapsed);
        ManualBrowserHost.Content = state.Browser;
        _browserPages.AddRange(state.Browsers);
        _gitApi = state.GitApi;
        _gitLifetime = state.GitLifetime;
        state.GitLifetime = null;
        _gitSurface = state.Git;
        foreach (var repository in state.Repositories)
        {
            GitRepositories.Children.Add(repository);
        }

        RebuildTabs();
    }

    /// <summary>Restores the selected surface after the new runtime's history is visible.</summary>
    /// <param name="pane">The active conversation.</param><returns>The selected file read, if any.</returns>
    private async Task RestoreWorkbenchFocusAsync(SessionPaneState pane)
    {
        if (!_workbenchStates.TryGetValue(pane.Engine, out var state) || !state.Open || state.Root != pane.Status.WorkspaceRoot)
        {
            return;
        }

        WorkbenchPanel.IsVisible = true;
        LayoutWorkbench();
        ExplorerPane.IsVisible = state.Explorer;
        if (state.BrowserVisible)
        {
            OpenManualBrowser();
        }
        else if (state.Terminal is { } terminal)
        {
            AttachTerminal(terminal, pane.Engine.Pages);
        }
        else if (state.Git is { } git)
        {
            ShowGitCanvas(git.Title, git.View);
        }
        else if (state.File is { } path)
        {
            await OpenEntryAsync(new WorkspaceFileEntry(path, false)).ConfigureAwait(true);
            if (state.Diff)
            {
                await ShowFileDiffAsync().ConfigureAwait(true);
            }
            else if (state.Source)
            {
                ShowSource();
            }

            FileEditor.CaretIndex = Math.Min(state.Caret, FileEditor.Text?.Length ?? 0);
        }
        else if (state.Launcher)
        {
            ShowWorkbenchLauncher();
        }

        ExplorerScroller.Offset = state.ExplorerOffset;
    }

    /// <summary>Releases cached widget lifetimes when the owning window closes.</summary>
    private void ReleaseWorkbenchPresentations()
    {
        foreach (var engine in _workbenchStates.Keys.ToArray())
        {
            ReleaseWorkbenchFor(engine);
        }

        if (_boundWorkspaceEngine is { } active)
        {
            ReleaseWorkbenchFor(active);
        }

        foreach (var page in ManualBrowserHost.Children.OfType<Browser.ManualWebPreview>().ToArray())
        {
            page.ClosePage();
            ManualBrowserHost.Release(page);
        }
    }

    /// <summary>Releases retained native guests only when their owning runtime is actually closed.</summary>
    /// <param name="engine">The closed runtime.</param>
    private void ReleaseWorkbenchFor(SessionEngine engine)
    {
        var pages = new List<Browser.ManualWebPreview>();
        if (_workbenchStates.Remove(engine, out var state))
        {
            state.GitLifetime?.Cancel();
            state.GitLifetime?.Dispose();
            pages.AddRange(state.Browsers);
        }

        if (ReferenceEquals(_boundWorkspaceEngine, engine))
        {
            pages.AddRange(_browserPages);
            _browserPages.Clear();
            _boundWorkspaceEngine = null;
            ResetGit();
        }

        foreach (var page in pages.Distinct())
        {
            page.ClosePage();
            ManualBrowserHost.Release(page);
        }

        _workbenchKeys.Remove(engine);
    }

    /// <summary>Visual state owned by one runtime, separate from its canonical workspace data.</summary>
    private sealed class WorkbenchPresentation
    {
        /// <summary>Gets or initializes the project owning this visual state.</summary>
        internal string Root { get; init; } = string.Empty;

        /// <summary>Gets or initializes a value indicating whether the workbench is open.</summary>
        internal bool Open
        {
            get; init;
        }

        /// <summary>Gets or initializes a value indicating whether the workbench fills the pane.</summary>
        internal bool Expanded
        {
            get; init;
        }

        /// <summary>Gets or initializes a value indicating whether hidden files are listed.</summary>
        internal bool Hidden
        {
            get; init;
        }

        /// <summary>Gets or initializes the selected file path.</summary>
        internal string? File
        {
            get; init;
        }

        /// <summary>Gets or initializes a value indicating whether source editing is selected.</summary>
        internal bool Source
        {
            get; init;
        }

        /// <summary>Gets or initializes a value indicating whether the file diff is selected.</summary>
        internal bool Diff
        {
            get; init;
        }

        /// <summary>Gets or initializes a value indicating whether the start surface is selected.</summary>
        internal bool Launcher
        {
            get; init;
        }

        /// <summary>Gets or initializes the retained browser visual.</summary>
        internal Control? Browser
        {
            get; init;
        }

        /// <summary>Gets or initializes a value indicating whether the browser is selected.</summary>
        internal bool BrowserVisible
        {
            get; init;
        }

        /// <summary>Gets or initializes the selected kernel PTY identity.</summary>
        internal string? Terminal
        {
            get; init;
        }

        /// <summary>Gets or initializes the acknowledged directory listings.</summary>
        internal Dictionary<string, WorkspaceListing> Folders { get; init; } = [];

        /// <summary>Gets or initializes the folded explorer paths.</summary>
        internal HashSet<string> Collapsed { get; init; } = [];

        /// <summary>Gets or initializes a value indicating whether the explorer is visible.</summary>
        internal bool Explorer
        {
            get; init;
        }

        /// <summary>Gets or initializes the explorer scroll position.</summary>
        internal Vector ExplorerOffset
        {
            get; init;
        }

        /// <summary>Gets or initializes the file caret position.</summary>
        internal int Caret
        {
            get; init;
        }

        /// <summary>Gets or initializes the selected Git surface.</summary>
        internal (string Title, Control View)? Git
        {
            get; init;
        }

        /// <summary>Gets or initializes the repository runtime client.</summary>
        internal StudioPageClient? GitApi
        {
            get; init;
        }

        /// <summary>Gets or sets the retained repository widget lifetime.</summary>
        internal CancellationTokenSource? GitLifetime
        {
            get; set;
        }

        /// <summary>Gets or initializes the retained repository widgets.</summary>
        internal GitRepositoryView[] Repositories { get; init; } = [];

        /// <summary>Gets or initializes the independently open browser pages.</summary>
        internal Browser.ManualWebPreview[] Browsers { get; init; } = [];
    }
}
