using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Browser;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Unsaved editor buffers are scoped to the workspace that supplied the revision.</summary>
    private readonly Dictionary<(string Root, string Path), FileBuffer> _fileBuffers = new();

    /// <summary>The kernel workspace backing the explorer.</summary>
    private string _workspaceRoot = string.Empty;

    /// <summary>The last directory the explorer accepted.</summary>
    private string _directory = string.Empty;

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
        FilesTab.Content = MessageCatalog.Get(MessageKey.Common.Files, Language);
        ChangesTab.Content = MessageCatalog.Get(MessageKey.Common.Changes, Language);
        TabViewport.ScrollChanged += (_, _) => UpdateTabOverflow();
        TabViewport.SizeChanged += (_, _) => UpdateTabOverflow();
        FilesTab.Click += async (_, _) => await ListFilesAsync(_directory).ConfigureAwait(true);
        ChangesTab.Click += async (_, _) => await ListChangesAsync().ConfigureAwait(true);
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
        Label(ParentFolder, MessageKey.Common.Back);
        WorkbenchTitle.Text = MessageCatalog.Get(MessageKey.Common.Workbench, Language);
        WorkbenchView.Click += async (_, _) => await OpenWorkbenchAsync().ConfigureAwait(true);
        CloseWorkbench.Click += (_, _) =>
        {
            DetachTerminal();
            WorkbenchPanel.IsVisible = false;
            LayoutWorkbench();
            Composer.Focus();
        };
        ParentFolder.Click += async (_, _) =>
        {
            var separator = _directory.LastIndexOf('/');
            await ListFilesAsync(separator < 0 ? string.Empty : _directory[..separator]).ConfigureAwait(true);
        };
        FileEditor.TextChanged += (_, _) =>
        {
            if (_openFile is { } file)
            {
                file.Draft = FileEditor.Text ?? string.Empty;
                SaveFile.IsEnabled = file.CanSave;
            }
        };
        SaveFile.Click += OnSaveFile;
    }

    /// <summary>Invalidates pending reads when the session changes workspace.</summary>
    /// <param name="root">Workspace root from session status.</param>
    private void BindWorkspace(string root)
    {
        if (_workspaceRoot == root)
        {
            return;
        }

        DetachTerminal();
        ReleasePreview();
        _fileRead++;
        ManualBrowserHost.Content = null;
        HideBrowser();
        _workspaceRoot = root;
        _directory = string.Empty;
        _openFile = null;
        RenderedFile.IsVisible = false;
        FileEditor.IsVisible = true;
        FileTabs.Children.Clear();
        PreviewFile.IsVisible = false;
        FileEditor.Text = string.Empty;
        FileEditor.IsReadOnly = true;
        SaveFile.IsEnabled = false;
        WorkspaceFiles.ItemsSource = null;
        WorkbenchPanel.IsVisible = false;
    }

    /// <summary>Opens the file explorer next to the active conversation.</summary>
    /// <returns>The initial directory read.</returns>
    private async Task OpenWorkbenchAsync()
    {
        HideBrowser();
        ShowAnalysis(false);
        WorkbenchPanel.IsVisible = true;
        LayoutWorkbench();
        EnterView(WorkbenchPanel);
        await ListFilesAsync(_directory).ConfigureAwait(true);
    }

    /// <summary>Gives a narrow workbench the pane; wide windows retain the conversation beside it.</summary>
    private void LayoutWorkbench()
    {
        var room = LayoutRoot.Bounds.Width - LayoutRoot.ColumnDefinitions[0].Width.Value;
        var overlay = room <= 640;
        Grid.SetColumn(WorkbenchPanel, overlay ? 0 : 1);
        WorkbenchPanel.Width = overlay ? double.NaN : Math.Min(510, room - 246);
        ConversationArea.IsVisible = !(overlay && WorkbenchPanel.IsVisible);
    }

    /// <summary>Lists paths using the kernel's workspace grants.</summary>
    /// <param name="directory">The relative directory to open.</param>
    /// <returns>The directory update.</returns>
    private async Task ListFilesAsync(string directory)
    {
        if (_engine is null)
        {
            return;
        }

        var ticket = ++_fileRead;
        try
        {
            var list = await _engine.Pages.FilesAsync(directory, string.Empty, _closing.Token).ConfigureAwait(true);
            if (ticket != _fileRead || _closing.IsCancellationRequested)
            {
                return;
            }

            _directory = directory;
            ParentFolder.IsEnabled = directory.Length > 0;
            var folders = (list.Directories ?? Array.Empty<string>()).Select(path => new FileEntry(path, true));
            WorkspaceFiles.ItemsSource = folders.Concat((list.Files ?? Array.Empty<string>()).Select(path => new FileEntry(path, false))).ToList();
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

    /// <summary>Opens the chosen directory or versioned file.</summary>
    /// <param name="sender">The explorer.</param>
    /// <param name="e">The selection change.</param>
    private async void OnFileSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_engine is null || WorkspaceFiles.SelectedItem is not FileEntry entry)
        {
            return;
        }

        if (entry.Directory)
        {
            await ListFilesAsync(entry.Path).ConfigureAwait(true);
            return;
        }

        await OpenEntryAsync(entry).ConfigureAwait(true);
    }

    /// <summary>Opens a listed workspace file or a diff, ignoring superseded reads.</summary>
    /// <param name="entry">The entry selected in the explorer or tab strip.</param>
    /// <returns>The opened entry.</returns>
    private async Task OpenEntryAsync(FileEntry entry)
    {
        if (_engine is null)
        {
            return;
        }

        HideBrowser();
        var ticket = ++_fileRead;
        var key = (_workspaceRoot, entry.Path);
        try
        {
            if (entry.Change)
            {
                var diff = await _engine.Pages.DiffAsync(entry.Path, _closing.Token).ConfigureAwait(true);
                if (ticket == _fileRead)
                {
                    _openFile = null;
                    ShowDiff((string?)diff["diff"]);
                    WorkbenchTitle.Text = entry.Path;
                }

                return;
            }

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

    /// <summary>Displays the real working-tree changes from the current workspace.</summary>
    /// <returns>The changed-path list.</returns>
    private async Task ListChangesAsync()
    {
        if (_engine is null)
        {
            return;
        }

        var ticket = ++_fileRead;
        try
        {
            var paths = await _engine.Pages.ChangesAsync(_closing.Token).ConfigureAwait(true);
            if (ticket == _fileRead)
            {
                ParentFolder.IsEnabled = false;
                WorkspaceFiles.ItemsSource = paths.Select(path => new FileEntry(path, false, true)).ToList();
                if (paths.Count == 0)
                {
                    WorkbenchTitle.Text = MessageCatalog.Get(MessageKey.Workbench.NoChanges, Language);
                }
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
    private void OpenManualBrowser(string? html = null)
    {
        DetachTerminal();
        ShowAnalysis(false);
        WorkbenchPanel.IsVisible = true;
        LayoutWorkbench();
        if (ManualBrowserHost.Content is not ManualWebPreview preview)
        {
            Uri.TryCreate(_engine?.Host, UriKind.Absolute, out var kernel);
            preview = new ManualWebPreview(kernel, Language);
            ManualBrowserHost.Content = preview;
        }

        BackToEditor.IsVisible = html is not null;
        WorkspaceBody.IsVisible = false;
        ParentFolder.IsVisible = false;
        SaveFile.IsVisible = html is not null;
        ManualBrowserHost.IsVisible = true;
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
        BackToEditor.IsVisible = false;
        ManualBrowserHost.IsVisible = false;
        ParentFolder.IsVisible = true;
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

    /// <summary>Restores editing against the original file revision.</summary>
    private void ShowSource()
    {
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
            var diff = await _engine.Pages.DiffAsync(file.Original.Path, _closing.Token).ConfigureAwait(true);
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
        ReleasePreview();
        FileEditor.IsVisible = false;
        RenderedFile.Content = view;
        RenderedFile.IsVisible = true;
        SaveFile.IsEnabled = false;
        PreviewFile.IsVisible = false;
    }

    /// <summary>Builds tabs for files opened in this workspace.</summary>
    private void RebuildTabs()
    {
        FileTabs.Children.Clear();
        Control? selected = null;
        foreach (var (key, buffer) in _fileBuffers.Where(pair => pair.Key.Root == _workspaceRoot))
        {
            var group = new StackPanel { Orientation = Orientation.Horizontal, Tag = key.Path };
            if (ReferenceEquals(buffer, _openFile))
            {
                selected = group;
            }

            var tab = new Button
            {
                Content = Path.GetFileName(key.Path),
                IsEnabled = !ReferenceEquals(buffer, _openFile),
                FontSize = 12,
            };
            ToolTip.SetTip(tab, key.Path);
            tab.Click += async (_, _) => await OpenEntryAsync(new FileEntry(key.Path, false)).ConfigureAwait(true);
            var close = new Button { Content = "×", Padding = new Avalonia.Thickness(4, 0), FontSize = 12 };
            Label(close, MessageKey.Workbench.CloseTab);
            close.Click += (_, _) =>
            {
                if (!buffer.Dirty && !buffer.Saving)
                {
                    CloseFile(key, buffer);
                    return;
                }

                var prompt = new MenuFlyout();
                var save = new MenuItem { Header = MessageCatalog.Get(MessageKey.Common.Save, Language), IsEnabled = buffer.CanSave };
                save.Click += async (_, _) =>
                {
                    if (await SaveBufferAsync(buffer).ConfigureAwait(true) && !buffer.Dirty)
                    {
                        CloseFile(key, buffer);
                    }
                };
                var discard = new MenuItem { Header = MessageCatalog.Get(MessageKey.Workbench.Discard, Language), IsEnabled = !buffer.Saving };
                discard.Click += (_, _) => CloseFile(key, buffer);
                prompt.Items.Add(save);
                prompt.Items.Add(discard);
                prompt.Items.Add(new MenuItem { Header = MessageCatalog.Get(MessageKey.Common.Cancel, Language) });
                prompt.ShowAt(close);
            };
            group.Children.Add(tab);
            group.Children.Add(close);
            FileTabs.Children.Add(group);
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(
            () =>
            {
                selected?.BringIntoView();
                UpdateTabOverflow();
            },
            Avalonia.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Lists tabs clipped by the strip, using their measured positions and scroll offset.</summary>
    private void UpdateTabOverflow()
    {
        var width = TabViewport.Viewport.Width;
        var needsMenu = width > 0 && TabViewport.Extent.Width > width + (TabOverflow.IsVisible ? TabOverflow.Bounds.Width : 0) + 0.5;
        TabOverflow.IsVisible = needsMenu;
        if (!needsMenu || TabOverflow.Flyout is not MenuFlyout menu)
        {
            return;
        }

        var offset = TabViewport.Offset.X;
        var clipped = FileTabs.Children.Where(c => c.Bounds.X < offset - 0.5 || c.Bounds.Right > offset + width + 0.5).Select(c => (string)c.Tag!).ToArray();
        if (clipped.SequenceEqual(menu.Items.OfType<MenuItem>().Select(item => (string)item.Tag!)))
        {
            return;
        }

        menu.Items.Clear();
        foreach (var path in clipped)
        {
            var item = new MenuItem { Header = Path.GetFileName(path), Tag = path };
            ToolTip.SetTip(item, path);
            item.Click += async (_, _) => await OpenEntryAsync(new FileEntry(path, false)).ConfigureAwait(true);
            menu.Items.Add(item);
        }

        TabOverflow.Content = "+" + clipped.Length.ToString(System.Globalization.CultureInfo.CurrentCulture) + " ▾";
        Avalonia.Automation.AutomationProperties.SetName(TabOverflow, string.Join(", ", clipped.Select(Path.GetFileName)));
    }

    /// <summary>Closes the chosen tab after any required save or explicit discard.</summary>
    /// <param name="key">Workspace and path.</param>
    /// <param name="file">The buffer confirmed by the reader.</param>
    private void CloseFile((string Root, string Path) key, FileBuffer file)
    {
        if (file.Saving || !_fileBuffers.Remove(key))
        {
            return;
        }

        if (ReferenceEquals(_openFile, file))
        {
            _fileRead++;
            _openFile = null;
            FileEditor.Text = string.Empty;
            FileEditor.IsReadOnly = true;
            SaveFile.IsEnabled = false;
            ReleasePreview();
            FileEditor.IsVisible = true;
            PreviewFile.IsVisible = false;
            WorkbenchTitle.Text = MessageCatalog.Get(MessageKey.Common.Workbench, Language);
            WorkspaceFiles.SelectedItem = null;
        }

        RebuildTabs();
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
        SaveFile.IsEnabled = false;
        try
        {
            var submitted = file.Original with
            {
                Content = file.Draft,
            };
            file.Original = await _engine.Pages.SaveFileAsync(submitted, _closing.Token).ConfigureAwait(true);
            if (file.Draft == submitted.Content)
            {
                file.Draft = file.Original.Content;
                if (ReferenceEquals(_openFile, file))
                {
                    FileEditor.Text = file.Draft;
                }
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

    /// <summary>An explorer entry with the exact path the host returned.</summary>
    /// <param name="Path">Workspace-relative path.</param>
    /// <param name="Directory">Whether selecting the row opens a directory.</param>
    /// <param name="Change">Whether the path opens its working-tree diff.</param>
    private sealed record FileEntry(string Path, bool Directory, bool Change = false)
    {
        /// <inheritdoc/>
        public override string ToString() => System.IO.Path.GetFileName(Path) + (Directory ? "/" : string.Empty);
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
