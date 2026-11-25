using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Workbench;
using System;
using System.IO;
using System.Linq;
using FileEntry = Reasonix.Studio.Shell.Workbench.WorkspaceFileEntry;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Builds tabs for files opened in this workspace.</summary>
    /// <param name="newSurface">Identity of a surface just opened by the reader.</param>
    /// <param name="animateSelection">Whether this rebuild is part of the current workbench interaction.</param>
    private void RebuildTabs(object? newSurface = null, bool animateSelection = true)
    {
        FileTabs.Children.Clear();
        Control? selected = null;
        var current = WorkbenchStart.IsVisible || BackgroundJobHost.IsVisible || SubagentRunHost.IsVisible || (ManualBrowserHost.IsVisible && !BackToEditor.IsVisible)
            ? null
            : SelectedWorkbenchDocument;
        foreach (var (key, buffer) in _workbench.Buffers.Where(pair => pair.Key.Root == WorkspaceBufferScope))
        {
            var group = new WorkbenchHeaderTab(Path.GetFileName(key.Path), "File", current == key.Path, Language) { Tag = key.Path };
            group.ColorFile(key.Path);
            group.ShowDirty(buffer.Dirty);
            if (current == key.Path)
            {
                selected = group;
            }

            var tab = group.PickButton;
            ToolTip.SetTip(tab, key.Path);
            tab.Click += async (_, _) => await OpenEntryAsync(new FileEntry(key.Path, false)).ConfigureAwait(true);
            tab.ContextFlyout = BuildWorkspaceFileMenu(new FileEntry(key.Path, false));
            var close = group.CloseButton;
            close.Click += async (_, _) =>
            {
                if (!buffer.Dirty && !buffer.Saving)
                {
                    if (await group.LeaveAsync(_motionLifetime.Token).ConfigureAwait(true)
                        && !buffer.Dirty && _workbench.Buffers.TryGetValue(key, out var currentBuffer) && ReferenceEquals(buffer, currentBuffer))
                    {
                        CloseFile(key, buffer);
                    }

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
            FileTabs.Children.Add(group);
            if (Equals(newSurface, key.Path) && Classes.Contains("motion"))
            {
                group.Enter(_motionLifetime.Token);
            }
        }

        if (_gitSurface is { } git)
        {
            var isActive = !WorkbenchStart.IsVisible && !ManualBrowserHost.IsVisible && !BackgroundJobHost.IsVisible && !SubagentRunHost.IsVisible && RenderedFile.IsVisible
                && ReferenceEquals(git.View, RenderedFile.Content);
            var group = new WorkbenchHeaderTab(git.Title, "Changes", isActive, Language) { Tag = git };
            if (git.View is GitChangesView changes)
            {
                group.ShowCount(changes);
            }

            group.PickButton.Click += (_, _) => ShowGitCanvas(git.Title, git.View);
            var close = group.CloseButton;
            Avalonia.Automation.AutomationProperties.SetAutomationId(close, "action:workbench.close-git");
            close.Click += async (_, _) =>
            {
                if (!await group.LeaveAsync(_motionLifetime.Token).ConfigureAwait(true) || _gitSurface != git)
                {
                    return;
                }

                var visible = ReferenceEquals(git.View, RenderedFile.Content);
                _gitSurface = null;
                if (visible)
                {
                    ReleasePreview();
                    FileCanvas.RowDefinitions[0].Height = new GridLength(40);
                    if (_workbench.Buffers.LastOrDefault(pair => pair.Key.Root == WorkspaceBufferScope) is { Value: not null } last)
                    {
                        await OpenEntryAsync(new FileEntry(last.Key.Path, false)).ConfigureAwait(true);
                        if (last.Value.Dirty)
                        {
                            ShowSource();
                        }
                    }
                    else
                    {
                        FileEditor.IsVisible = true;
                        FileEditor.IsReadOnly = true;
                        FileEditor.Text = string.Empty;
                    }
                }

                RebuildTabs();
            };
            FileTabs.Children.Insert(0, group);
            if (ReferenceEquals(newSurface, git.View) && Classes.Contains("motion"))
            {
                group.Enter(_motionLifetime.Token);
            }

            if (isActive)
            {
                selected = group;
            }
        }

        selected = AppendSurfaceTabs(newSurface) ?? selected;
        selected = AppendJobOutputTabs(newSurface) ?? selected;
        selected = AppendSubagentRunTabs(newSurface) ?? selected;
        if (_workbenchLauncherSelected)
        {
            selected = new WorkbenchHeaderTab(MessageCatalog.Get("workbench.new_tab_label", Language), "New", true, Language) { Classes = { "launcher" } };
            FileTabs.Children.Add(selected);
        }

        object? canvasKey = selected switch
        {
            { Tag: WorkbenchSurfaceTarget target } => target.Canvas,
            { Tag: ValueTuple<string, Control> } => "changes:",
            { Tag: string path } => "file:" + path,
            not null when _workbenchLauncherSelected => "launcher:",
            _ => null,
        };
        UpdateWorkbenchCanvas(canvasKey, animateSelection);

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
        var clipped = FileTabs.Children.Where(c => c.Tag is not null && (c.Bounds.X < offset - 0.5 || c.Bounds.Right > offset + width + 0.5)).Select(c => c.Tag).ToArray();
        if (clipped.SequenceEqual(menu.Items.OfType<MenuItem>().Select(item => item.Tag)))
        {
            return;
        }

        menu.Items.Clear();
        foreach (var target in clipped)
        {
            var item = new MenuItem { Tag = target };
            if (target is string path)
            {
                item.Header = Path.GetFileName(path);
                ToolTip.SetTip(item, path);
                item.Click += async (_, _) => await OpenEntryAsync(new FileEntry(path, false)).ConfigureAwait(true);
            }
            else if (target is ValueTuple<string, Control> surface)
            {
                item.Header = surface.Item1;
                item.Click += (_, _) =>
                {
                    if (_gitSurface == surface)
                    {
                        ShowGitCanvas(surface.Item1, surface.Item2);
                    }
                };
            }

            if (target is WorkbenchSurfaceTarget browser)
            {
                item.Header = browser.Title;
                item.Click += (_, _) => browser.Open();
            }

            menu.Items.Add(item);
        }

        TabOverflowCount.Text = "+" + clipped.Length.ToString(System.Globalization.CultureInfo.CurrentCulture);
        Avalonia.Automation.AutomationProperties.SetName(TabOverflow, string.Join(", ", menu.Items.OfType<MenuItem>().Select(i => i.Header)));
    }

    /// <summary>Closes the chosen tab after any required save or explicit discard.</summary>
    /// <param name="key">Workspace and path.</param>
    /// <param name="file">The buffer confirmed by the reader.</param>
    private void CloseFile((string Root, string Path) key, FileBuffer file)
    {
        if (file.Saving || !_workbench.Buffers.Remove(key))
        {
            return;
        }

        if (SelectedWorkbenchDocument == key.Path)
        {
            _fileRead++;
            _openFile = null;
            HideFileError();
            FileEditor.Text = string.Empty;
            FileEditor.IsReadOnly = true;
            SaveFile.IsEnabled = false;
            ReleasePreview();
            FileEditor.IsVisible = true;
            SaveFile.IsVisible = true;
            PreviewFile.IsVisible = false;
            SetFileBarTitle(MessageCatalog.Get(MessageKey.Common.Workbench, Language), path: false);
            WorkspaceFiles.SelectedItem = null;
        }

        RefreshWorkbenchLauncher();
        RebuildTabs();
    }
}
