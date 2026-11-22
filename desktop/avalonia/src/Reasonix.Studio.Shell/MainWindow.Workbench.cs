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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FileEntry = Reasonix.Studio.Shell.Workbench.WorkspaceFileEntry;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Unsaved editor buffers are scoped to the workspace that supplied the revision.</summary>
    private readonly Dictionary<(string Root, string Path), FileBuffer> _fileBuffers = new();

    /// <summary>The kernel workspace backing the explorer.</summary>
    private string _workspaceRoot = string.Empty;

    /// <summary>Whether the reader expanded the workbench to the pane.</summary>
    private bool _fullWorkbench;

    /// <summary>The version currently shown in the editor.</summary>
    private FileBuffer? _openFile;

    /// <summary>Orders reads across directory, selection and workspace changes.</summary>
    private long _fileRead;

    /// <summary>Wires the native workspace explorer, editor and save command.</summary>
    private void InitializeWorkbench()
    {
        BrowserTabTitle.Text = MessageCatalog.Get(MessageKey.Workbench.Browser, Language);
        BrowserAction.Header = BrowserTabTitle.Text;
        BrowserAction.Click += (_, _) => OpenManualBrowser();
        BackToEditor.Content = MessageCatalog.Get(MessageKey.Common.Edit, Language);
        BackToEditor.Click += (_, _) => ShowSource();
        ExplorerHeading.Text = MessageCatalog.Get(MessageKey.Workbench.Explorer, Language);
        Label(ToggleExplorer, MessageKey.Workbench.Explorer);
        SetUpWorkspaceMenus();
        FullWorkbench.Click += (_, _) =>
        {
            _fullWorkbench = !_fullWorkbench;
            Label(FullWorkbench, _fullWorkbench ? "workbench.exit_expanded_workbench" : "workbench.expand_workbench");
            LayoutWorkbench();
        };
        ToggleExplorer.Click += (_, _) =>
        {
            if (ManualBrowserHost.IsVisible)
            {
                HideBrowser();
                ExplorerPane.IsVisible = true;
            }
            else
            {
                ExplorerPane.IsVisible = !ExplorerPane.IsVisible;
            }
        };
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
        SaveFile.Content = MessageCatalog.Get(MessageKey.Common.Save, Language);
        Label(CloseWorkbench, MessageKey.Common.Close);
        WorkbenchTitle.Text = MessageCatalog.Get(MessageKey.Common.Workbench, Language);
        WorkbenchView.Click += async (_, _) => await OpenWorkbenchAsync().ConfigureAwait(true);
        CloseWorkbench.Click += (_, _) =>
        {
            DetachTerminal();
            WorkbenchPanel.IsVisible = false;
            LayoutWorkbench();
            Composer.Focus();
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
        FileEditor.ContextMenu = BuildFileEditorContextMenu();
    }

    /// <summary>Builds the file editor's text actions, including quoting into the composer.</summary>
    /// <returns>The context menu.</returns>
    private ContextMenu BuildFileEditorContextMenu()
    {
        var menu = new ContextMenu();
        var quote = new MenuItem { Header = MessageCatalog.Get("workbench.add_to_conversation", Language) };
        quote.Click += (_, _) => InsertBlockQuote(FileEditor.SelectedText);
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
        var scope = BufferScopeFor(root);
        if (_workspaceRoot == root && ReferenceEquals(_boundWorkspaceEngine, _engine))
        {
            _workspaceBufferScope = scope;
            return;
        }

        CaptureWorkbenchPresentation();
        DetachTerminal();
        ReleasePreview();
        _fileRead++;
        ManualBrowserHost.Content = null;
        HideBrowser();
        ResetGit();
        _workspaceRoot = root;
        _boundWorkspaceEngine = _engine;
        _workspaceBufferScope = scope;
        ResetFileTree();
        _openFile = null;
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
    }

    /// <summary>Opens the file explorer next to the active conversation.</summary>
    /// <returns>The initial directory read.</returns>
    private async Task OpenWorkbenchAsync()
    {
        HideBrowser();
        ShowAnalysis(false);
        WorkbenchPanel.IsVisible = true;
        RevealWorkspace.IsVisible = _engine?.IsRemote != true;
        LayoutWorkbench();
        EnterView(WorkbenchPanel);
        await Task.WhenAll(ListFilesAsync(), LoadGitAsync()).ConfigureAwait(true);
        if (_openFile is null && _gitSurface is null && !TerminalSurface.IsVisible)
        {
            ShowWorkbenchLauncher();
        }
    }

    /// <summary>Gives a narrow workbench the pane; wide windows retain the conversation beside it.</summary>
    private void LayoutWorkbench()
    {
        var room = LayoutRoot.Bounds.Width - LayoutRoot.ColumnDefinitions[0].Width.Value;
        var overlay = _fullWorkbench || room <= 640;
        Grid.SetColumn(WorkbenchPanel, overlay ? 0 : 1);
        WorkbenchPanel.Width = overlay ? double.NaN : Math.Min(510, room - 246);
        ConversationArea.IsVisible = !(overlay && WorkbenchPanel.IsVisible);
        var stacked = LayoutRoot.Bounds.Width <= 840;
        var docked = WorkbenchPanel.IsVisible && !overlay;
        ComposerArea.Margin = new Avalonia.Thickness(docked ? 39 : stacked ? 10 : 24, 0);
        ConversationArea.Margin = new Avalonia.Thickness(0, 0, docked ? 2 : 0, 0);
        Conversation.SetReadingInset(docked ? 45 : stacked ? 10 : 24);
    }

    /// <summary>Opens a listed workspace file or a diff, ignoring superseded reads.</summary>
    /// <param name="entry">The entry selected in the explorer or tab strip.</param>
    /// <returns>The opened entry.</returns>
    private async Task OpenEntryAsync(FileEntry entry)
    {
        WorkbenchStart.IsVisible = false;
        if (_engine is null)
        {
            return;
        }

        HideBrowser();
        var ticket = ++_fileRead;
        FileCanvas.RowDefinitions[0].Height = new GridLength(40);
        FileMore.IsVisible = true;
        var key = (WorkspaceBufferScope, entry.Path);
        try
        {
            if (!_fileBuffers.TryGetValue(key, out var buffer) || !buffer.Dirty)
            {
                var info = await _engine.Pages.PreviewInfoAsync(entry.Path, _closing.Token).ConfigureAwait(true);
                if (ticket != _fileRead || _closing.IsCancellationRequested)
                {
                    return;
                }

                if ((string?)info["kind"] == "image")
                {
                    var bytes = await _engine.Pages.PreviewImageAsync(entry.Path, (string?)info["version"] ?? string.Empty, _closing.Token).ConfigureAwait(true);
                    if (ticket == _fileRead && bytes is not null)
                    {
                        _openFile = null;
                        SetReadOnlyBadge(null);
                        using var input = new MemoryStream(bytes);
                        ShowPreview(new Image { Source = new Bitmap(input), Stretch = Stretch.Uniform });
                        WorkbenchTitle.Text = entry.Path;
                    }

                    return;
                }

                var file = await _engine.Pages.FileAsync(entry.Path, _closing.Token).ConfigureAwait(true);
                if (ticket != _fileRead || _closing.IsCancellationRequested)
                {
                    return;
                }

                buffer = new FileBuffer(file, (string?)info["kind"]);
                _fileBuffers[key] = buffer;
            }

            _openFile = buffer;
            SetReadOnlyBadge(buffer);
            SelectWorkspaceFile(entry.Path);
            ReleasePreview();
            FileModes.IsVisible = true;
            FileEditor.IsVisible = true;
            PreviewFile.IsVisible = buffer.Kind is "markdown" or "html";
            PreviewFile.Content = MessageCatalog.Get(MessageKey.Common.Preview, Language);
            RebuildTabs();
            FileEditor.Text = buffer.Draft;
            FileEditor.IsReadOnly = buffer.Original.ReadOnly;
            WorkbenchTitle.Text = entry.Path;
            SaveFile.IsEnabled = buffer.CanSave;
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
                ReportWorkbenchError(error);
            }
        }
    }

    /// <summary>Opens an isolated, user-operated system browser in the workbench.</summary>
    /// <param name="html">Optional workspace HTML already read under the file grant.</param>
    /// <param name="newPage">Whether an explicit launcher action creates another browser tab.</param>
    private void OpenManualBrowser(string? html = null, bool newPage = false)
    {
        WorkbenchStart.IsVisible = false;
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

        if (html is null && !_browserPages.Contains(preview))
        {
            _browserPages.Add(preview);
        }

        BackToEditor.IsVisible = html is not null;
        WorkspaceBody.IsVisible = false;
        SaveFile.IsVisible = html is not null;
        ManualBrowserHost.IsVisible = true;
        RebuildTabs();
        if (html is null)
        {
            WorkbenchTitle.Text = MessageCatalog.Get(MessageKey.Workbench.Browser, Language);
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
        FileCanvas.RowDefinitions[0].Height = new GridLength(40);
        FileMore.IsVisible = _openFile is not null;
        BackToEditor.IsVisible = false;
        ManualBrowserHost.IsVisible = false;
        SaveFile.IsVisible = true;
        WorkspaceBody.IsVisible = true;
        if (_openFile is { } file)
        {
            WorkbenchTitle.Text = file.Original.Path;
            PreviewFile.IsVisible = file.Kind is "markdown" or "html";
            PreviewFile.Content = MessageCatalog.Get(file.Kind == "markdown" ? MessageKey.Workbench.Read : MessageKey.Common.Preview, Language);
            SaveFile.IsEnabled = file.CanSave;
        }
    }

    /// <summary>Draws a file or change-list diff through the same native component.</summary>
    /// <param name="patch">Unified diff from the active workspace.</param>
    private void ShowDiff(string? patch)
    {
        var preview = new Workbench.DiffPreview();
        preview.Show(string.IsNullOrEmpty(patch) ? MessageCatalog.Get(MessageKey.Workbench.NoChanges, Language) : patch);
        ShowPreview(preview);
    }

    /// <summary>Marks the active presentation of the file in the toolbar.</summary>
    /// <param name="active">Selected mode.</param>
    private void SelectFileMode(Button active)
    {
        foreach (var mode in new[] { PreviewFile, EditFile, DiffFile })
        {
            mode.Classes.Set("active", mode == active);
        }
    }

    /// <summary>Shows why a granted file cannot be saved.</summary>
    /// <param name="file">Current buffer, or null when no file is open.</param>
    private void SetReadOnlyBadge(FileBuffer? file)
    {
        var readOnly = file?.Original.ReadOnly == true;
        ReadOnlyBadge.IsVisible = readOnly;
        ReadOnlyBadgeText.Text = readOnly ? MessageCatalog.Get("workbench.read_only_from_a_folder", Language) : string.Empty;
        ToolTip.SetTip(ReadOnlyBadge, readOnly ? file?.Original.Root ?? string.Empty : string.Empty);
    }

    /// <summary>Restores editing against the original file revision.</summary>
    private void ShowSource()
    {
        WorkbenchStart.IsVisible = false;
        _fileRead++;
        HideBrowser();
        ReleasePreview();
        FileEditor.IsVisible = true;
        if (_openFile is { } file)
        {
            PreviewFile.IsVisible = file.Kind is "markdown" or "html";
            PreviewFile.Content = MessageCatalog.Get(file.Kind == "markdown" ? MessageKey.Workbench.Read : MessageKey.Common.Preview, Language);
            SaveFile.IsEnabled = file.CanSave;
            FileEditor.Focus();
        }

        SelectFileMode(EditFile);
    }

    /// <summary>Shows the draft as Markdown without replacing its editor.</summary>
    /// <param name="file">Current buffer.</param>
    private void ShowMarkdown(FileBuffer file)
    {
        _fileRead++;
        var text = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
        MarkdownAnswer.Show(text, file.Draft);
        ShowPreview(text);
        FileModes.IsVisible = true;
        PreviewFile.IsVisible = true;
        PreviewFile.Content = MessageCatalog.Get(MessageKey.Workbench.Read, Language);
        SelectFileMode(PreviewFile);
        SaveFile.IsEnabled = file.CanSave;
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

            ShowDiff((string?)diff["diff"]);
            FileModes.IsVisible = true;
            PreviewFile.IsVisible = file.Kind is "markdown" or "html";
            SelectFileMode(DiffFile);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (ticket == _fileRead)
            {
                ReportWorkbenchError(error);
            }
        }
    }

    /// <summary>Releases the previous image and preview visual.</summary>
    private void ReleasePreview()
    {
        DetachTerminal();
        FileModes.IsVisible = _openFile is not null;
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
        WorkbenchStart.IsVisible = false;
        ReleasePreview();
        FileEditor.IsVisible = false;
        RenderedFile.Padding = view.Classes.Contains("git-board") ? default : new Avalonia.Thickness(10);
        RenderedFile.VerticalScrollBarVisibility = view.Classes.Contains("git-board") ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        RenderedFile.Content = view;
        RenderedFile.IsVisible = true;
        SaveFile.IsEnabled = false;
        PreviewFile.IsVisible = false;
    }

    /// <summary>Saves the selected revision while preserving edits made during the write.</summary>
    /// <param name="sender">The save button.</param>
    /// <param name="e">The click.</param>
    private async void OnSaveFile(object? sender, RoutedEventArgs e)
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
            ReportWorkbenchError(error);
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
    private void ReportWorkbenchError(Exception error)
    {
        ShowRefusal(error);
        ToolTip.SetTip(WorkbenchTitle, ErrorText.Text);
        WorkbenchTitle.Text = ErrorText.Text;
    }

    /// <summary>A text buffer and the revision it was read against.</summary>
    /// <param name="file">File as read from the kernel.</param>
    /// <param name="kind">Preview type selected by the kernel.</param>
    private sealed class FileBuffer(WorkspaceFile file, string? kind)
    {
        /// <summary>Gets the kernel-declared preview type.</summary>
        public string? Kind { get; } = kind;

        /// <summary>Gets or sets the version used for the next save.</summary>
        public WorkspaceFile Original { get; set; } = file;

        /// <summary>Gets or sets the text in the editor.</summary>
        public string Draft { get; set; } = file.Content;

        /// <summary>Gets or sets a value indicating whether a write is in flight.</summary>
        public bool Saving
        {
            get; set;
        }

        /// <summary>Gets a value indicating whether this buffer holds unsaved edits.</summary>
        public bool Dirty => Draft != Original.Content;

        /// <summary>Gets a value indicating whether a write can be requested.</summary>
        public bool CanSave => !Saving && !Original.ReadOnly && Dirty;
    }
}
