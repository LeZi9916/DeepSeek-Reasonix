using Avalonia.Automation;
using Avalonia.Controls;
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

    /// <summary>Connects the missing workspace entry points to their canonical commands.</summary>
    private void InitializeWorkbenchActions()
    {
        InitializeFileActions();
        NewSurface.Flyout = null;
        NewSurface.Click += (_, _) => ShowWorkbenchLauncher();
        WorkbenchStart.FilesRequested += async () =>
        {
            WorkbenchStart.IsVisible = false;
            ShowSource();
            ExplorerPane.IsVisible = true;
            await ListFilesAsync().ConfigureAwait(true);
            WorkbenchSearch.Focus();
        };
        WorkbenchStart.BrowserRequested += () =>
        {
            WorkbenchStart.IsVisible = false;
            OpenManualBrowser(newPage: true);
        };
        WorkbenchStart.TerminalRequested += () =>
        {
            WorkbenchStart.IsVisible = false;
            OnTerminals(null, new RoutedEventArgs());
        };
        WorkbenchStart.FileRequested += async path =>
        {
            WorkbenchStart.IsVisible = false;
            await OpenEntryAsync(new WorkspaceFileEntry(path, false)).ConfigureAwait(true);
        };
        Label(HiddenFiles, "workbench.show_hidden_files");
        HiddenFiles.Click += async (_, _) =>
        {
            _showHiddenFiles = !_showHiddenFiles;
            HiddenFiles.Classes.Set("active", _showHiddenFiles);
            HiddenSlash.IsVisible = !_showHiddenFiles;
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

    /// <summary>Shows the same start surface from the plus button and an empty workbench.</summary>
    private void ShowWorkbenchLauncher()
    {
        HideBrowser();
        DetachTerminal();
        FileEditor.IsVisible = false;
        RenderedFile.IsVisible = false;
        FileModes.IsVisible = false;
        FileMore.IsVisible = false;
        SaveFile.IsVisible = false;
        WorkbenchStart.IsVisible = true;
        WorkbenchStart.Show(GitRepositories.Children.OfType<GitRepositoryView>().Sum(view => view.ChangeCount), _fileBuffers.Keys.Where(key => key.Root == WorkspaceBufferScope).Select(key => key.Path).Reverse().Take(5), Language);
    }
}
