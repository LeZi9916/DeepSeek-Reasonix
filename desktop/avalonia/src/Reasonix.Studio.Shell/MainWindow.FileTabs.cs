using Avalonia.Controls;
using Avalonia.Layout;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Pages.I18n;
using System;
using System.IO;
using System.Linq;
using FileEntry = Reasonix.Studio.Shell.Workbench.WorkspaceFileEntry;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Builds tabs for files opened in this workspace.</summary>
    private void RebuildTabs()
    {
        FileTabs.Children.Clear();
        Control? selected = null;
        foreach (var (key, buffer) in _fileBuffers.Where(pair => pair.Key.Root == WorkspaceBufferScope))
        {
            var group = new StackPanel { Orientation = Orientation.Horizontal, Tag = key.Path };
            group.Classes.Add("wb-tab-frame");
            group.Classes.Set("active", ReferenceEquals(buffer, _openFile));
            if (ReferenceEquals(buffer, _openFile))
            {
                selected = group;
            }

            var tab = new Button
            {
                Content = Path.GetFileName(key.Path),
                IsEnabled = true,
                [!TextBlock.FontSizeProperty] = StudioTypography.Size(12),
            };
            tab.Classes.Add("wb-tab");
            tab.Classes.Set("active", ReferenceEquals(buffer, _openFile));
            ToolTip.SetTip(tab, key.Path);
            tab.Click += async (_, _) => await OpenEntryAsync(new FileEntry(key.Path, false)).ConfigureAwait(true);
            tab.ContextFlyout = BuildWorkspaceFileMenu(new FileEntry(key.Path, false));
            var close = new Button { Content = "×", Padding = new Avalonia.Thickness(4, 0), [!TextBlock.FontSizeProperty] = StudioTypography.Size(12) };
            close.Classes.Add("wb-tab-close");
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

        if (_gitSurface is { } git)
        {
            var group = new StackPanel { Orientation = Orientation.Horizontal, Tag = git };
            group.Classes.Add("wb-tab-frame");
            group.Classes.Set("active", ReferenceEquals(git.View, RenderedFile.Content));
            var tab = new Button { Content = git.Title };
            tab.Classes.Add("wb-tab");
            tab.Classes.Set("active", ReferenceEquals(git.View, RenderedFile.Content));
            tab.Click += (_, _) => ShowGitCanvas(git.Title, git.View);
            var close = new Button { Content = "×" };
            close.Classes.Add("wb-tab-close");
            Label(close, MessageKey.Workbench.CloseTab);
            Avalonia.Automation.AutomationProperties.SetAutomationId(close, "action:workbench.close-git");
            close.Click += async (_, _) =>
            {
                var visible = ReferenceEquals(git.View, RenderedFile.Content);
                _gitSurface = null;
                if (visible)
                {
                    ReleasePreview();
                    FileCanvas.RowDefinitions[0].Height = new GridLength(40);
                    if (_fileBuffers.LastOrDefault(pair => pair.Key.Root == WorkspaceBufferScope) is { Value: not null } last)
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
            group.Children.Add(tab);
            group.Children.Add(close);
            FileTabs.Children.Insert(0, group);
            if (ReferenceEquals(git.View, RenderedFile.Content))
            {
                selected = group;
            }
        }

        AppendSurfaceTabs();
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
        var clipped = FileTabs.Children.Where(c => c.Bounds.X < offset - 0.5 || c.Bounds.Right > offset + width + 0.5).Select(c => c.Tag).ToArray();
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

        TabOverflow.Content = "+" + clipped.Length.ToString(System.Globalization.CultureInfo.CurrentCulture) + " ▾";
        Avalonia.Automation.AutomationProperties.SetName(TabOverflow, string.Join(", ", menu.Items.OfType<MenuItem>().Select(i => i.Header)));
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
            HideFileError();
            FileEditor.Text = string.Empty;
            FileEditor.IsReadOnly = true;
            SaveFile.IsEnabled = false;
            ReleasePreview();
            FileEditor.IsVisible = true;
            PreviewFile.IsVisible = false;
            SetFileBarTitle(MessageCatalog.Get(MessageKey.Common.Workbench, Language), path: false);
            WorkspaceFiles.SelectedItem = null;
        }

        RebuildTabs();
    }
}
