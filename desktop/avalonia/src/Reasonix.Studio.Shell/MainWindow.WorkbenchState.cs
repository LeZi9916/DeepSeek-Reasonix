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
    /// <summary>Owns workspace presentation and retained file buffers.</summary>
    private readonly WorkbenchSession _workbench = new();

    /// <summary>Gets the active runtime's stable editor-buffer namespace.</summary>
    private string WorkspaceBufferScope => _workbench.BufferScope.Length > 0 ? _workbench.BufferScope : _workspaceRoot;

    /// <summary>Retains widgets without cancelling a background runtime's work.</summary>
    private void CaptureWorkbenchPresentation()
    {
        if (_workbench.BoundEngine is not { } engine)
        {
            return;
        }

        FileEditor.Capture();
        _workbench.Capture(engine, new WorkbenchPresentation
        {
            Root = _workspaceRoot,
            Open = WorkbenchPanel.IsVisible,
            Expanded = _fullWorkbench,
            Hidden = _showHiddenFiles,
            File = SelectedWorkbenchDocument,
            Source = FileEditor.IsVisible,
            Diff = DiffFile.Classes.Contains("active"),
            Launcher = _workbenchLauncherSelected,
            Browser = ManualBrowserHost.Content as Control,
            Browsers = _browserPages.ToArray(),
            AgentBrowserTabs = _agentBrowserTabs,
            AgentBrowserTarget = VisibleAgentPage?.Target,
            Jobs = _jobOutputViews.ToArray(),
            Agents = _subagentRuns.ToArray(),
            SubagentRun = SubagentRunHost.IsVisible ? SubagentRunHost.Content as SubagentRunView : null,
            JobOutput = BackgroundJobHost.IsVisible ? BackgroundJobHost.Content as BackgroundJobOutputView : null,
            BrowserVisible = ManualBrowserHost.IsVisible,
            Terminal = _terminal.Id,
            Folders = new Dictionary<string, WorkspaceListing>(_fileDirectories, StringComparer.Ordinal),
            Collapsed = new HashSet<string>(_fileCollapsed, StringComparer.Ordinal),
            Explorer = ExplorerPane.IsVisible,
            ExplorerOffset = ExplorerScroller.Offset,
            Git = _gitSurface,
            GitApi = _gitApi,
            GitLifetime = _gitLifetime,
            Repositories = GitRepositories.Children.OfType<GitRepositoryView>().ToArray(),
        });
        _gitLifetime = null;
        _gitApi = null;
        _gitSurface = null;
        GitRepositories.Children.Clear();
    }

    /// <summary>Mounts retained workspace state before fresh reads can update its widgets.</summary>
    private void RestoreWorkbenchPresentation()
    {
        _agentBrowserRead++;
        _agentBrowserTabs = [];
        _browserPages.Clear();
        _jobOutputViews.Clear();
        _subagentRuns.Clear();
        _showHiddenFiles = _workspacePreferences.ShowHiddenFiles;
        _fullWorkbench = false;
        if (_engine is not { } engine || !_workbench.TryRead(engine, out var state) || state.Root != _workspaceRoot)
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
        _agentBrowserTabs = state.AgentBrowserTabs;
        _jobOutputViews.AddRange(state.Jobs);
        _subagentRuns.AddRange(state.Agents);
        _gitApi = state.GitApi;
        _gitLifetime = state.GitLifetime;
        state.GitLifetime = null;
        _gitSurface = state.Git;
        foreach (var repository in state.Repositories)
        {
            GitRepositories.Children.Add(repository);
        }

        RebuildTabs(animateSelection: false);
    }

    /// <summary>Restores the selected surface after the new runtime's history is visible.</summary>
    /// <param name="pane">The active conversation.</param><returns>The selected file read, if any.</returns>
    private async Task RestoreWorkbenchFocusAsync(SessionPaneState pane)
    {
        if (!_workbench.TryRead(pane.Engine, out var state) || !state.Open || state.Root != pane.Status.WorkspaceRoot)
        {
            return;
        }

        WorkbenchPanel.IsVisible = true;
        LayoutWorkbench();
        if (state.SubagentRun is { } run)
        {
            SelectSubagentRun(run, animateSelection: false);
        }
        else if (state.JobOutput is { } jobOutput)
        {
            SelectJobOutput(jobOutput, animateSelection: false);
        }
        else if (state.BrowserVisible)
        {
            OpenManualBrowser(animateSelection: false);
        }
        else if (state.AgentBrowserTarget is { } browserTarget && _agentBrowserTabs.Any(tab => tab.Target == browserTarget))
        {
            SelectAgentBrowserTab(browserTarget, animateSelection: false);
        }
        else if (state.Terminal is { } terminal)
        {
            AttachTerminal(terminal, pane.Engine.Pages);
        }
        else if (state.File is { } path)
        {
            await OpenEntryAsync(new WorkspaceFileEntry(path, false), animateTab: false).ConfigureAwait(true);
            if (!ReferenceEquals(_engine, pane.Engine))
            {
                return;
            }

            if (state.Diff)
            {
                await ShowFileDiffAsync().ConfigureAwait(true);
            }
            else if (state.Source)
            {
                ShowSource();
            }
        }
        else if (state.Git is { } git)
        {
            ShowGitCanvas(git.Title, git.View, animateSelection: false);
        }

        if (ReferenceEquals(_engine, pane.Engine))
        {
            ExplorerPane.IsVisible = state.Explorer;
            SetWorkbenchLauncher(state.Launcher, animateSelection: false);
            ExplorerScroller.Offset = state.ExplorerOffset;
        }
    }

    /// <summary>Releases cached widget lifetimes when the owning window closes.</summary>
    private void ReleaseWorkbenchPresentations()
    {
        foreach (var engine in _workbench.Runtimes())
        {
            ReleaseWorkbenchFor(engine);
        }

        if (_workbench.BoundEngine is { } active)
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
        var pages = _workbench.Release(engine).ToList();

        if (ReferenceEquals(_workbench.BoundEngine, engine))
        {
            pages.AddRange(_browserPages);
            _browserPages.Clear();
            HideJobOutput();
            BackgroundJobHost.Content = null;
            _jobOutputViews.Clear();
            HideSubagentRun();
            SubagentRunHost.Content = null;
            _subagentRuns.Clear();
            _workbench.BoundEngine = null;
            ResetGit();
        }

        foreach (var page in pages.Distinct())
        {
            page.ClosePage();
            ManualBrowserHost.Release(page);
        }
    }
}
