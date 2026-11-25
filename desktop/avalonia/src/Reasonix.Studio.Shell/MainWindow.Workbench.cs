using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Browser;
using Reasonix.Studio.Shell.Workbench;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FileEntry = Reasonix.Studio.Shell.Workbench.WorkspaceFileEntry;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The kernel workspace backing the explorer.</summary>
    private string _workspaceRoot = string.Empty;

    /// <summary>Whether the reader expanded the workbench to the pane.</summary>
    private bool _fullWorkbench;

    /// <summary>The version currently shown in the editor.</summary>
    private FileBuffer? _openFile;

    /// <summary>Orders reads across directory, selection and workspace changes.</summary>
    private long _fileRead;

    /// <summary>The read or save the in-canvas error can retry.</summary>
    private Func<Task>? _workbenchRetry;

    /// <summary>Wires the native workspace explorer, editor and save command.</summary>
    private void InitializeWorkbench()
    {
        _terminal.Exited += code => SetFileBarTitle(WorkbenchTitle.Text + " · " + code, path: false);
        _terminal.Failed += error => ReportWorkbenchError(error);
        InitializeBrowserSleep();
        BrowserAction.Header = MessageCatalog.Get(MessageKey.Workbench.Browser, Language);
        BrowserAction.Click += (_, _) => OpenManualBrowser();
        BackToEditor.Content = MessageCatalog.Get(MessageKey.Common.Edit, Language);
        BackToEditor.Click += (_, _) => ShowSource();
        ExplorerHeading.Text = MessageCatalog.Get(MessageKey.Workbench.Explorer, Language);
        AutomationProperties.SetName(ToggleExplorer, MessageCatalog.Get(MessageKey.Common.Files, Language));
        SetUpWorkspaceMenus();
        FullWorkbench.Click += (_, _) =>
        {
            _fullWorkbench = !_fullWorkbench;
            LayoutWorkbench();
        };
        ToggleExplorer.Click += async (_, _) =>
        {
            if (_workbenchLauncherSelected)
            {
                await RevealWorkbenchExplorerAsync().ConfigureAwait(true);
            }
            else
            {
                ExplorerPane.IsVisible = !ExplorerPane.IsVisible;
            }
        };
        ExplorerPane.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty)
            {
                UpdateWorkbenchHeader();
            }
        };
        ManualBrowserHost.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty)
            {
                UpdateWorkbenchHeader();
            }
        };
        WorkbenchStart.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty)
            {
                UpdateWorkbenchHeader();
                RefreshJobOutputVisibility();
                RefreshSubagentRunVisibility();
            }
        };
        UpdateWorkbenchHeader();
        TerminalAction.Header = MessageCatalog.Get(MessageKey.Workbench.Terminal, Language);
        TerminalAction.Click += OnTerminals;
        EndTerminal.Content = MessageCatalog.Get(MessageKey.Workbench.CloseTab, Language);
        EndTerminal.Click += ConfirmTerminalClose;
        Label(FilesTab, MessageKey.Workbench.Explorer);
        FileListHeading.Text = MessageCatalog.Get(MessageKey.Common.Files, Language);
        Label(ToggleFileList, MessageKey.Common.Files);
        void ToggleFileListVisibility()
        {
            WorkspaceFiles.IsVisible = !WorkspaceFiles.IsVisible;
            NoWorkspaceFiles.IsVisible = WorkspaceFiles.IsVisible && WorkspaceFiles.ItemCount == 0;
            FileListChevron.RenderTransform = new RotateTransform(WorkspaceFiles.IsVisible ? 0 : -90);
        }

        ToggleFileList.Click += (_, _) => ToggleFileListVisibility();
        FilesTab.Click += async (_, _) => await ListFilesAsync().ConfigureAwait(true);
        WorkspaceFiles.AddHandler(Button.ClickEvent, OnWorkspaceFileClick);
        WorkspaceFiles.AddHandler(Avalonia.Input.InputElement.KeyDownEvent, OnFileTreeKeyDown, RoutingStrategies.Tunnel);
        NoWorkspaceFiles.Text = MessageCatalog.Get("workbench.no_matching_files", Language);
        WorkbenchSearch.PlaceholderText = MessageCatalog.Get("workbench.search_files", Language);
        WorkbenchSearch.TextChanged += async (_, _) =>
        {
            ClearWorkbenchSearch.IsVisible = !string.IsNullOrEmpty(WorkbenchSearch.Text);
            WorkbenchSearch.Padding = ClearWorkbenchSearch.IsVisible ? new Avalonia.Thickness(2, 0, 20, 0) : new Avalonia.Thickness(2, 0);
            if (WorkbenchPanel.IsVisible)
            {
                await ListFilesAsync().ConfigureAwait(true);
            }
        };
        ClearWorkbenchSearch.Click += async (_, _) =>
        {
            WorkbenchSearch.Text = string.Empty;
            await ListFilesAsync().ConfigureAwait(true);
            WorkbenchSearch.Focus();
        };
        var savedExplorerWidth = _workspacePreferences.ExplorerWidth;
        ExplorerPane.Width = savedExplorerWidth is >= 190 and <= 260 ? savedExplorerWidth : 190;
        ExplorerGrip.DragDelta += (_, e) =>
        {
            ExplorerPane.Width = Math.Clamp(ExplorerPane.Width + e.Vector.X, 190, 260);
        };
        ExplorerGrip.DragCompleted += (_, _) =>
        {
            _workspacePreferences = _workspacePreferences with
            {
                ExplorerWidth = ExplorerPane.Width,
            };
            if (_connectOnOpen)
            {
                _workspacePreferences.Save();
            }
        };
        ExplorerGrip.DoubleTapped += (_, _) =>
        {
            ExplorerPane.Width = 190;
            _workspacePreferences = _workspacePreferences with
            {
                ExplorerWidth = 190,
            };
            if (_connectOnOpen)
            {
                _workspacePreferences.Save();
            }
        };
        WorkspaceBody.SizeChanged += (_, e) =>
        {
            WorkspaceFiles.Margin = new Avalonia.Thickness(0, 0, ClientSize.Width > 900 ? 10 : 0, 0);
            foreach (var view in GitRepositories.Children.OfType<Workbench.GitRepositoryView>())
            {
                view.Fit(e.NewSize.Height);
            }
        };
        TabViewport.ScrollChanged += (_, _) => UpdateTabOverflow();
        TabViewport.SizeChanged += (_, _) => UpdateTabOverflow();
        EditFile.Content = MessageCatalog.Get(MessageKey.Common.Edit, Language);
        EditFile.Click += (_, _) => ShowSource();
        DiffFile.Click += async (_, _) => await ShowFileDiffAsync().ConfigureAwait(true);
        PreviewFile.Click += (_, _) =>
        {
            if (_openFile is { Kind: "html" } html)
            {
                OpenManualBrowser(html.Draft);
            }
            else if (_openFile is { Kind: "markdown" } markdown)
            {
                ShowMarkdown(markdown);
            }
        };
        WorkbenchView.Header = MessageCatalog.Get(MessageKey.Common.Workbench, Language);
        SaveFileLabel.Text = MessageCatalog.Get(MessageKey.Common.Save, Language);
        Label(CloseWorkbench, "app.close_workbench");
        SetFileBarTitle(MessageCatalog.Get(MessageKey.Common.Workbench, Language), path: false);
        WorkbenchView.Click += async (_, _) => await OpenWorkbenchAsync().ConfigureAwait(true);
        CloseWorkbench.Click += async (_, _) => await DismissWorkbenchAsync().ConfigureAwait(true);
        WorkbenchPanel.PropertyChanged += (_, change) =>
        {
            if (change.Property != IsVisibleProperty)
            {
                return;
            }

            RefreshJobOutputVisibility();
            RefreshSubagentRunVisibility();
            if (!WorkbenchPanel.IsVisible)
            {
                CancelWorkbenchDismissal();
                CancelWorkbenchEntrance();
                SetWorkbenchPose(1, 0);
            }
        };
        FileEditor.TextChanged += (_, _) =>
        {
            if (_openFile is { } file)
            {
                file.Draft = FileEditor.Text ?? string.Empty;
                SaveFile.IsEnabled = file.CanSave;
                DrawFileStatus();
            }
        };
        SaveFile.Click += OnSaveFile;
        FileEditor.KeyDown += async (_, e) =>
        {
            var primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
            if (e.Key == Key.S && e.KeyModifiers == primary)
            {
                e.Handled = true;
                await SaveOpenFileAsync().ConfigureAwait(true);
            }
        };
        FileEditor.ContextMenu = BuildFileEditorContextMenu();
        FileErrorRetry.Content = MessageCatalog.Get("common.read_again", Language);
        FileErrorClose.Content = MessageCatalog.Get(MessageKey.Workbench.CloseTab, Language);
        FileErrorRetry.Click += async (_, _) =>
        {
            var retry = _workbenchRetry;
            HideFileError();
            if (retry is not null)
            {
                await retry().ConfigureAwait(true);
            }
        };
        FileErrorClose.Click += (_, _) =>
        {
            HideFileError();
            if (SelectedWorkbenchDocument is { } path && _workbench.Buffers.TryGetValue((WorkspaceBufferScope, path), out var file))
            {
                CloseFile((WorkspaceBufferScope, path), file);
            }
        };
    }

    /// <summary>Hides the in-canvas file error and forgets its retry.</summary>
    private void HideFileError()
    {
        FileErrorPanel.IsVisible = false;
        _workbenchRetry = null;
    }

    /// <summary>Builds the file editor's text actions, including quoting into the composer.</summary>
    /// <returns>The context menu.</returns>
    private ContextMenu BuildFileEditorContextMenu()
    {
        var menu = new ContextMenu();
        var quote = new MenuItem { Header = MessageCatalog.Get("workbench.add_to_conversation", Language) };
        quote.Click += (_, _) => AddQuoteChip(FileEditor.SelectedText, _openFile?.Original.Path ?? string.Empty);
        var cut = new MenuItem
        {
            Header = MessageCatalog.Get("workbench.cut", Language),
            InputGesture = new KeyGesture(Key.X, KeyModifiers.Control),
        };
        cut.Click += (_, _) => FileEditor.Cut();
        var copy = new MenuItem
        {
            Header = MessageCatalog.Get("common.copy", Language),
            InputGesture = new KeyGesture(Key.C, KeyModifiers.Control),
        };
        copy.Click += (_, _) => FileEditor.Copy();
        var paste = new MenuItem
        {
            Header = MessageCatalog.Get("workbench.paste", Language),
            InputGesture = new KeyGesture(Key.V, KeyModifiers.Control),
        };
        paste.Click += (_, _) => FileEditor.Paste();
        var selectAll = new MenuItem
        {
            Header = MessageCatalog.Get("workbench.select_all", Language),
            InputGesture = new KeyGesture(Key.A, KeyModifiers.Control),
        };
        selectAll.Click += (_, _) => FileEditor.SelectAll();
        var undo = new MenuItem
        {
            Header = MessageCatalog.Get("workbench.undo", Language),
            InputGesture = new KeyGesture(Key.Z, KeyModifiers.Control),
        };
        undo.Click += (_, _) => FileEditor.Undo();
        var redo = new MenuItem
        {
            Header = MessageCatalog.Get("workbench.redo", Language),
            InputGesture = new KeyGesture(Key.Z, KeyModifiers.Control | KeyModifiers.Shift),
        };
        redo.Click += (_, _) => FileEditor.Redo();
        menu.Items.Add(quote);
        menu.Items.Add(new Separator());
        menu.Items.Add(cut);
        menu.Items.Add(copy);
        menu.Items.Add(paste);
        menu.Items.Add(selectAll);
        menu.Items.Add(new Separator());
        menu.Items.Add(undo);
        menu.Items.Add(redo);
        menu.Opening += (_, _) =>
        {
            var selected = FileEditor.SelectionStart != FileEditor.SelectionEnd;
            var editable = !FileEditor.IsReadOnly;
            quote.IsEnabled = selected;
            cut.IsEnabled = editable && selected;
            copy.IsEnabled = selected;
            paste.IsEnabled = editable;
            undo.IsEnabled = editable && FileEditor.CanUndo;
            redo.IsEnabled = editable && FileEditor.CanRedo;
        };
        return menu;
    }

    /// <summary>Mounts the workbench retained by this runtime and canonical project.</summary>
    /// <param name="root">Workspace root from session status.</param>
    private void BindWorkspace(string root)
    {
        var scope = _workbench.ScopeFor(_engine, root);
        if (_workspaceRoot == root && ReferenceEquals(_workbench.BoundEngine, _engine))
        {
            _workbench.BufferScope = scope;
            return;
        }

        CaptureWorkbenchPresentation();
        SetWorkbenchLauncher(false);
        DetachTerminal();
        ReleasePreview();
        _fileRead++;
        ManualBrowserHost.Content = null;
        HideBrowser();
        BackgroundJobHost.Content = null;
        SubagentRunHost.Content = null;
        ResetGit();
        _workspaceRoot = root;
        _workbench.BoundEngine = _engine;
        _workbench.BufferScope = scope;
        ResetFileTree();
        UpdateWorkbenchCanvas(null, animate: false);
        _openFile = null;
        ExplorerPane.IsVisible = false;
        SetReadOnlyBadge(null);
        RenderedFile.IsVisible = false;
        FileEditor.IsVisible = true;
        FileTabs.Children.Clear();
        PreviewFile.IsVisible = false;
        FileEditor.Text = string.Empty;
        FileEditor.IsReadOnly = true;
        SaveFile.IsEnabled = false;
        WorkspaceFiles.ItemsSource = null;
        WorkbenchPanel.IsVisible = false;
        RestoreWorkbenchPresentation();
        RefreshWorkbenchLauncher();
    }

    /// <summary>Opens the file explorer next to the active conversation.</summary>
    /// <returns>The initial directory read.</returns>
    private async Task OpenWorkbenchAsync()
    {
        RevealWorkbench(animate: true);
        await Task.WhenAll(ListFilesAsync(), LoadGitAsync()).ConfigureAwait(true);
        RefreshWorkbenchLauncher(refreshContents: true);
    }

    /// <summary>Opens the workbench and plays the shared entrance for an explicit action.</summary>
    /// <param name="animate">Whether to play the opening motion.</param>
    private void RevealWorkbench(bool animate)
    {
        var reopening = _workbenchDismiss is not null;
        var opening = !WorkbenchPanel.IsVisible || reopening;
        var prior = reopening ? (WorkbenchPanel.Width, WorkbenchPanel.Opacity, Offset: ReadWorkbenchOffset()) : default;
        CancelWorkbenchDismissal();
        ShowAnalysis(false);
        WorkbenchPanel.IsVisible = true;
        RevealWorkspace.IsVisible = _engine?.IsRemote != true;
        LayoutWorkbench();
        if (opening && animate && Classes.Contains("motion"))
        {
            var docked = !double.IsNaN(WorkbenchPanel.Width);
            var width = reopening ? prior.Width : 0;
            var opacity = reopening ? prior.Opacity : docked ? 1 : 0;
            var offset = reopening ? prior.Offset : docked ? 0 : 24;
            _ = EnterWorkbenchAsync(width, opacity, offset);
        }
    }

    /// <summary>Gives a narrow workbench the pane; wide windows retain the conversation beside it.</summary>
    private void LayoutWorkbench()
    {
        var room = LayoutRoot.Bounds.Width - LayoutRoot.ColumnDefinitions[0].Width.Value;
        var overlay = _fullWorkbench || room <= 640;
        if (overlay && _workbenchEntrance is { Docked: true } entrance)
        {
            entrance.Lifetime.Cancel();
        }

        UpdateWorkbenchHeader();
        Grid.SetColumn(WorkbenchPanel, overlay ? 0 : 1);
        WorkbenchPanel.Width = overlay ? double.NaN : Math.Min(510, room - 246);
        WorkbenchPanel.Margin = new Avalonia.Thickness(overlay ? 2 : 0, 8, 8, 8);
        ConversationArea.IsVisible = !(overlay && WorkbenchPanel.IsVisible);
        var stacked = LayoutRoot.Bounds.Width <= 840;
        Conversation.SetMessageRailAvailable(!stacked);
        var docked = WorkbenchPanel.IsVisible && !overlay;
        ComposerArea.Margin = new Avalonia.Thickness(stacked ? 10 : docked ? 39 : 24, 0);
        ConversationArea.Margin = new Avalonia.Thickness(0, 0, docked ? 2 : 0, 0);
        Conversation.SetReadingInset(docked ? 45 : stacked ? 10 : 24);
    }

    /// <summary>Reflects the explorer and expanded workbench state in the header controls.</summary>
    private void UpdateWorkbenchHeader()
    {
        var explorer = ExplorerPane.IsVisible && !WorkbenchStart.IsVisible;
        ToggleExplorer.Classes.Set("active", explorer);
        ToolTip.SetTip(ToggleExplorer, MessageCatalog.Get(explorer ? "workbench.hide_file_tree" : "workbench.show_file_tree", Language));
        FullWorkbench.Classes.Set("active", _fullWorkbench);
        Label(FullWorkbench, _fullWorkbench ? "workbench.exit_expanded_workbench" : "workbench.expand_workbench");
    }

    /// <summary>Opens a listed workspace file or a diff, ignoring superseded reads.</summary>
    /// <param name="entry">The entry selected in the explorer or tab strip.</param>
    /// <param name="animateTab">Whether this open came from user navigation.</param>
    /// <returns>The opened entry.</returns>
    private async Task OpenEntryAsync(FileEntry entry, bool animateTab = true)
    {
        SetWorkbenchLauncher(false, animateSelection: animateTab);
        if (_engine is not { } engine)
        {
            return;
        }

        HideBrowser();
        var ticket = ++_fileRead;
        FileCanvas.RowDefinitions[0].Height = new GridLength(40);
        FileMore.IsVisible = true;
        var key = (WorkspaceBufferScope, entry.Path);
        var newlyOpened = animateTab && !_workbench.Buffers.ContainsKey(key);
        try
        {
            if (!_workbench.Buffers.TryGetValue(key, out var buffer) || !buffer.Dirty)
            {
                var info = await engine.Pages.PreviewInfoAsync(entry.Path, _closing.Token).ConfigureAwait(true);
                if (ticket != _fileRead || _closing.IsCancellationRequested)
                {
                    return;
                }

                if ((string?)info["kind"] == "office")
                {
                    await ShowOfficePreviewAsync(engine, entry, info, ticket).ConfigureAwait(true);
                    return;
                }

                if ((string?)info["kind"] == "image")
                {
                    var bytes = await engine.Pages.PreviewContentAsync(entry.Path, (string?)info["version"] ?? string.Empty, _closing.Token).ConfigureAwait(true);
                    if (ticket == _fileRead && bytes is not null)
                    {
                        _openFile = null;
                        SetReadOnlyBadge(null);
                        using var input = new MemoryStream(bytes);
                        ShowPreview(new Image { Source = new Bitmap(input), Stretch = Stretch.Uniform });
                        SetFileBarTitle(entry.Path, path: true);
                    }

                    return;
                }

                var file = await engine.Pages.FileAsync(entry.Path, _closing.Token).ConfigureAwait(true);
                if (ticket != _fileRead || _closing.IsCancellationRequested)
                {
                    return;
                }

                buffer = new FileBuffer(file, (string?)info["kind"]);
                _workbench.Buffers[key] = buffer;
            }

            _openFile = buffer;
            RefreshWorkbenchLauncher();
            SetReadOnlyBadge(buffer);
            SelectWorkspaceFile(entry.Path);
            ReleasePreview();
            FileModes.IsVisible = true;
            FileEditor.IsVisible = true;
            DrawFileToolbar(buffer);
            RebuildTabs(newlyOpened ? entry.Path : null, animateSelection: animateTab);
            FileEditor.Text = buffer.Draft;
            FileEditor.IsReadOnly = buffer.Original.ReadOnly;
            SetFileBarTitle(buffer.Original, entry.Path);
            if (buffer.Kind == "markdown")
            {
                ShowMarkdown(buffer);
            }
            else
            {
                SelectFileMode(EditFile);
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (ticket == _fileRead)
            {
                ReportWorkbenchError(error, () => OpenEntryAsync(entry));
            }
        }
    }

    /// <summary>Opens an isolated, user-operated system browser in the workbench.</summary>
    /// <param name="html">Optional workspace HTML already read under the file grant.</param>
    /// <param name="newPage">Whether an explicit launcher action creates another browser tab.</param>
    /// <param name="animateSelection">Whether the browser was selected in the current workbench.</param>
    private void OpenManualBrowser(string? html = null, bool newPage = false, bool animateSelection = true)
    {
        CancelWorkbenchDismissal();
        HideJobOutput();
        HideSubagentRun();
        SetWorkbenchLauncher(false, animateSelection);
        DetachTerminal();
        ShowAnalysis(false);
        WorkbenchPanel.IsVisible = true;
        LayoutWorkbench();
        if (ManualBrowserHost.Content is not ManualWebPreview preview || newPage)
        {
            Uri.TryCreate(_engine?.Host, UriKind.Absolute, out var kernel);
            preview = new ManualWebPreview(kernel, Language);
            ManualBrowserHost.Content = preview;
        }

        var newlyOpened = html is null && !_browserPages.Contains(preview);
        if (newlyOpened)
        {
            _browserPages.Add(preview);
        }

        BackToEditor.IsVisible = html is not null;
        if (html is null)
        {
            ExplorerPane.IsVisible = false;
        }

        SaveFile.IsVisible = html is not null;
        ManualBrowserHost.IsVisible = true;
        preview.Wake();
        RebuildTabs(newlyOpened && animateSelection ? preview : null, animateSelection);
        if (html is null)
        {
            SetFileBarTitle(MessageCatalog.Get(MessageKey.Workbench.Browser, Language), path: false);
            PreviewFile.IsVisible = false;
            SaveFile.IsEnabled = false;
            Avalonia.Threading.Dispatcher.UIThread.Post(preview.FocusAddress, Avalonia.Threading.DispatcherPriority.Input);
        }
        else
        {
            PreviewFile.IsVisible = true;
            PreviewFile.Content = MessageCatalog.Get(MessageKey.Common.Edit, Language);
            preview.ShowDocument(html);
        }
    }

    /// <summary>Restores the native file explorer without navigating the guest.</summary>
    private void HideBrowser()
    {
        HideJobOutput();
        HideSubagentRun();
        FileCanvas.RowDefinitions[0].Height = new GridLength(40);
        FileMore.IsVisible = _openFile is not null;
        BackToEditor.IsVisible = false;
        ManualBrowserHost.IsVisible = false;
        SaveFile.IsVisible = true;
        if (_openFile is { } file)
        {
            SetFileBarTitle(file.Original, file.Original.Path);
            DrawFileToolbar(file);
        }
    }

    /// <summary>Shows the file's numbered patch with a return to its source editor.</summary>
    /// <param name="path">The open file's path.</param><param name="patch">Unified diff from the active workspace.</param>
    private void ShowDiff(string path, string? patch)
    {
        var preview = new Workbench.GitPatchView();
        AutomationProperties.SetAutomationId(preview, "class:workbench-diff");
        preview.Show(
            patch ?? string.Empty,
            path,
            MessageCatalog.Get(MessageKey.Workbench.NoChanges, Language),
            truncated: null,
            open: () =>
            {
                ShowSource();
                return Task.CompletedTask;
            });
        ShowPreview(preview);
        if (!preview.HasHunks)
        {
            RenderedFile.Padding = new Avalonia.Thickness(8);
        }
    }

    /// <summary>Reads a brush from the window's theme resources.</summary>
    /// <param name="key">Resource key.</param>
    /// <returns>The brush, or a muted fallback.</returns>
    private IBrush ResourceBrush(string key) =>
        this.TryFindResource(key, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    /// <summary>Restores editing against the original file revision.</summary>
    private void ShowSource()
    {
        HideFileError();
        SetWorkbenchLauncher(false);
        _fileRead++;
        HideBrowser();
        ReleasePreview();
        FileEditor.IsVisible = true;
        if (_openFile is { } file)
        {
            DrawFileToolbar(file);
            FileEditor.Focus();
        }

        SelectFileMode(EditFile);
    }

    /// <summary>Shows the draft as Markdown without replacing its editor.</summary>
    /// <param name="file">Current buffer.</param>
    private void ShowMarkdown(FileBuffer file)
    {
        _fileRead++;
        var text = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeight.Normal,
            [!TextBlock.FontSizeProperty] = StudioTypography.Resource("StudioReadingSize"),
            [!TextBlock.LineHeightProperty] = StudioTypography.Resource("WorkbenchReadingHeight"),
        };
        MarkdownAnswer.Show(text, file.Draft);
        ShowPreview(text);
        RenderedFile.Padding = new Avalonia.Thickness(32, 24, 32, 48);
        FileModes.IsVisible = true;
        DrawFileToolbar(file);
        SelectFileMode(PreviewFile);
    }

    /// <summary>Reads the selected file's diff without losing its current draft.</summary>
    /// <returns>The diff read.</returns>
    private async Task ShowFileDiffAsync()
    {
        if (_engine is null || _openFile is not { } file)
        {
            return;
        }

        var ticket = ++_fileRead;
        try
        {
            var (repo, relative) = LocateRepository(file.Original.Path);
            var diff = await _engine.Pages.GitDiffAsync(repo, relative, _closing.Token).ConfigureAwait(true);
            if (ticket != _fileRead || file != _openFile)
            {
                return;
            }

            ShowDiff(file.Original.Path, (string?)diff["diff"]);
            FileModes.IsVisible = true;
            DrawFileToolbar(file);
            SelectFileMode(DiffFile);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (ticket == _fileRead)
            {
                ReportWorkbenchError(error, ShowFileDiffAsync);
            }
        }
    }

    /// <summary>Releases the previous image and preview visual.</summary>
    private void ReleasePreview()
    {
        DetachTerminal();
        FileModes.IsVisible = _openFile is not null;
        EditFile.IsVisible = DiffFile.IsVisible = true;
        if (RenderedFile.Content is Image { Source: Bitmap bitmap })
        {
            bitmap.Dispose();
        }

        RenderedFile.Content = null;
        RenderedFile.IsVisible = false;
    }

    /// <summary>Replaces the reading surface without destroying text drafts.</summary>
    /// <param name="view">Image, rendered text or diff.</param>
    private void ShowPreview(Control view)
    {
        HideFileError();
        SetWorkbenchLauncher(false);
        ReleasePreview();
        FileEditor.IsVisible = false;
        var fillsCanvas = view.Classes.Contains("git-board") || view is Workbench.OfficeDocumentPreview or Workbench.GitPatchView { HasHunks: true };
        RenderedFile.Padding = fillsCanvas ? default : new Avalonia.Thickness(10);
        RenderedFile.VerticalScrollBarVisibility = fillsCanvas ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        RenderedFile.Content = view;
        RenderedFile.IsVisible = true;
        SaveFile.IsEnabled = false;
        PreviewFile.IsVisible = false;
    }

    /// <summary>Saves the selected revision while preserving edits made during the write.</summary>
    /// <param name="sender">The save button.</param>
    /// <param name="e">The click.</param>
    private async void OnSaveFile(object? sender, RoutedEventArgs e) => await SaveOpenFileAsync().ConfigureAwait(true);

    /// <summary>Uses the same save operation for the editor shortcut and the toolbar.</summary>
    /// <returns>The save request, if the open buffer can be saved.</returns>
    private async Task SaveOpenFileAsync()
    {
        if (_openFile is { } file)
        {
            await SaveBufferAsync(file).ConfigureAwait(true);
        }
    }

    /// <summary>Saves a buffer and keeps any subsequent edits dirty.</summary>
    /// <param name="file">The buffer to save.</param>
    /// <returns>Whether the host accepted the snapshot.</returns>
    private async Task<bool> SaveBufferAsync(FileBuffer file)
    {
        if (_engine is null || !file.CanSave)
        {
            return false;
        }

        file.Saving = true;
        var engine = _engine;
        SaveFile.IsEnabled = false;
        try
        {
            var submitted = file.Original with
            {
                Content = file.Draft,
            };
            file.Original = await engine.Pages.SaveFileAsync(submitted, _closing.Token).ConfigureAwait(true);
            if (file.Draft == submitted.Content)
            {
                file.Draft = file.Original.Content;
                if (ReferenceEquals(_openFile, file))
                {
                    FileEditor.Text = file.Draft;
                }
            }

            if (ReferenceEquals(engine, _engine) && WorkbenchPanel.IsVisible)
            {
                await LoadGitAsync().ConfigureAwait(true);
            }

            return true;
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception error)
        {
            ReportWorkbenchError(error, async () => await SaveBufferAsync(file).ConfigureAwait(true));
            return false;
        }
        finally
        {
            file.Saving = false;
            SaveFile.IsEnabled = _openFile?.CanSave == true;
            DrawFileStatus();
        }
    }

    /// <summary>Keeps the editor intact when a read or save is refused.</summary>
    /// <param name="error">The host's refusal.</param>
    /// <param name="retry">The read or save that can be attempted again.</param>
    private void ReportWorkbenchError(Exception error, Func<Task>? retry = null)
    {
        _workbenchRetry = retry;
        FileErrorText.Text = DescribeError(error);
        FileErrorPanel.IsVisible = true;
        FileErrorRetry.Focus();
    }
}
