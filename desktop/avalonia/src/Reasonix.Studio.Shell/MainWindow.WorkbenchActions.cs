using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Workbench;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Whether this pane includes host-listed hidden files.</summary>
    private bool _showHiddenFiles;

    /// <summary>Whether the new-tab launcher is covering the retained surface.</summary>
    private bool _workbenchLauncherSelected;

    /// <summary>Connects the missing workspace entry points to their canonical commands.</summary>
    private void InitializeWorkbenchActions()
    {
        InitializeFileActions();
        NewSurface.Click += (_, _) => ToggleWorkbenchLauncher();
        WorkbenchPanel.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _workbenchLauncherSelected)
            {
                ToggleWorkbenchLauncher();
                NewSurface.Focus(NavigationMethod.Tab);
                e.Handled = true;
            }
        };
        WorkbenchStart.FilesRequested += async () => await RevealWorkbenchExplorerAsync().ConfigureAwait(true);
        WorkbenchStart.BrowserRequested += () => OpenManualBrowser(newPage: true);
        WorkbenchStart.TerminalRequested += () => OnTerminals(null, new RoutedEventArgs());
        WorkbenchStart.FileRequested += async path => await OpenEntryAsync(new WorkspaceFileEntry(path, false)).ConfigureAwait(true);
        foreach (var surface in new Control[] { RenderedFile, ManualBrowserHost, BackgroundJobHost, SubagentRunHost, TerminalSurface, FileErrorPanel, ExplorerPane })
        {
            surface.PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty)
                {
                    RefreshWorkbenchLauncher();
                }
            };
        }

        WorkbenchEmptyTitle.Text = MessageCatalog.Get("workbench.pick_a_file", Language);
        WorkbenchEmptyHint.Text = MessageCatalog.Get("workbench.pick_a_file_detail", Language);
        RefreshWorkbenchLauncher();
        Label(HiddenFiles, "workbench.show_hidden_files");
        _showHiddenFiles = _workspacePreferences.ShowHiddenFiles;
        HiddenFiles.Classes.Set("active", _showHiddenFiles);
        HiddenSlash.IsVisible = !_showHiddenFiles;
        HiddenFiles.Click += async (_, _) =>
        {
            _showHiddenFiles = !_showHiddenFiles;
            HiddenFiles.Classes.Set("active", _showHiddenFiles);
            HiddenSlash.IsVisible = !_showHiddenFiles;
            _workspacePreferences = _workspacePreferences with
            {
                ShowHiddenFiles = _showHiddenFiles,
            };
            if (_connectOnOpen)
            {
                _workspacePreferences.Save();
            }

            await ListFilesAsync().ConfigureAwait(true);
        };
        Label(OpenWorkspaceEditor, "workbench.open_the_workspace_in_a");
        OpenWorkspaceEditor.Click += async (_, _) =>
        {
            if (_engine is not { } engine)
            {
                return;
            }

            try
            {
                var reply = await engine.Pages.OpenWorkspaceEditorAsync(_closing.Token).ConfigureAwait(true);
                ToolTip.SetTip(OpenWorkspaceEditor, MessageCatalog.Get("workbench.opened_in_app", Language, ("app", (string?)reply["editor"] ?? string.Empty)));
            }
            catch (Exception error)
            {
                ReportWorkbenchError(error);
            }
        };
        Label(RevealWorkspace, "workbench.show_the_workspace_in_the");
        RevealWorkspace.Click += async (_, _) =>
        {
            if (_engine?.IsRemote == true)
            {
                return;
            }

            if (await new Host.AvaloniaHost(this).RevealPathAsync(_workspaceRoot, string.Empty).ConfigureAwait(true) is { } refusal)
            {
                ReportWorkbenchError(new InvalidOperationException(refusal.Error));
            }
        };
    }

    /// <summary>Switches between the new tab and the surface that remains mounted underneath it.</summary>
    private void ToggleWorkbenchLauncher()
    {
        _fileRead++;
        SetWorkbenchLauncher(!_workbenchLauncherSelected);
        if (_workbenchLauncherSelected)
        {
            WorkbenchStart.FocusFiles();
        }
    }

    /// <summary>Updates whether the launcher owns the selected tab.</summary>
    /// <param name="selected">Whether the reader deliberately opened the new tab.</param>
    /// <param name="animateSelection">Whether the launcher was selected in the current workbench.</param>
    private void SetWorkbenchLauncher(bool selected, bool animateSelection = true)
    {
        var changed = _workbenchLauncherSelected != selected;
        _workbenchLauncherSelected = selected;
        RefreshWorkbenchLauncher(refreshContents: selected);
        if (changed)
        {
            RebuildTabs(animateSelection: animateSelection);
        }
    }

    /// <summary>Shows the launcher on an empty canvas without selecting a new tab.</summary>
    /// <param name="refreshContents">Whether the workspace listing changed while the launcher was already shown.</param>
    private void RefreshWorkbenchLauncher(bool refreshContents = false)
    {
        var empty = _openFile is null && !ManualBrowserHost.IsVisible && !BackgroundJobHost.IsVisible && !SubagentRunHost.IsVisible && !RenderedFile.IsVisible
            && !TerminalSurface.IsVisible && !FileErrorPanel.IsVisible;
        var visible = _workbenchLauncherSelected || (empty && !ExplorerPane.IsVisible);
        WorkbenchEmpty.IsVisible = empty && ExplorerPane.IsVisible;
        if (visible && (refreshContents || !WorkbenchStart.IsVisible))
        {
            WorkbenchStart.Show(GitRepositories.Children.OfType<GitRepositoryView>().Sum(view => view.ChangeCount), _fileBuffers.Keys.Where(key => key.Root == WorkspaceBufferScope).Select(key => key.Path).Reverse().Take(5), Language);
        }

        WorkbenchStart.IsVisible = visible;
    }

    /// <summary>Opens the file tree beside the already selected surface.</summary>
    /// <returns>The workspace listing.</returns>
    private async Task RevealWorkbenchExplorerAsync()
    {
        ExplorerPane.IsVisible = true;
        SetWorkbenchLauncher(false);
        await ListFilesAsync().ConfigureAwait(true);
        if (WorkbenchSearch.IsEffectivelyVisible)
        {
            WorkbenchSearch.Focus();
        }
    }
}
