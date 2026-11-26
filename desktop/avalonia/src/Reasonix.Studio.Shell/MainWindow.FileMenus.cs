using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Workbench;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Connects explorer context menus and the editor's reading state.</summary>
    private void InitializeFileActions()
    {
        WorkspaceFiles.ContextRequested += (_, e) =>
        {
            var row = (e.Source as Control)?.FindAncestorOfType<WorkspaceFileRow>(includeSelf: true);
            if (row?.DataContext is not WorkspaceFileEntry entry)
            {
                return;
            }

            row.ContextFlyout = BuildWorkspaceFileMenu(entry);
            row.ContextFlyout.ShowAt(row);
            e.Handled = true;
        };
        FileEditor.TextArea.Caret.PositionChanged += (_, _) => DrawFileStatus();
        FileEditor.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty)
            {
                DrawFileStatus();
            }
        };
        WorkbenchStart.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty)
            {
                DrawFileStatus();
            }
        };
        RenderedFile.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty)
            {
                DrawFileStatus();
            }
        };
    }

    /// <summary>Builds identical commands for a file row and the open file toolbar.</summary>
    /// <param name="entry">The canonical workspace entry.</param><returns>The native file menu.</returns>
    private MenuFlyout BuildWorkspaceFileMenu(WorkspaceFileEntry entry)
    {
        var menu = new MenuFlyout();
        var engine = _engine;
        var root = _workspaceRoot;
        var reference = WorkspaceReference.Token(entry.Path + (entry.Directory ? "/" : string.Empty));
        Add(
            "workbench.add-to-chat",
            "workbench.add_to_conversation",
            async () =>
            {
                if (!ReferenceEquals(engine, _engine))
                {
                    return;
                }

                Composer.SelectedText = reference + " ";
                Composer.Focus();
                await Task.CompletedTask.ConfigureAwait(true);
            },
            reference.Length > 0);
        Add("workbench.copy-ref", "workbench.copy_reference", () => Copy(reference), reference.Length > 0);
        var absolute = WorkspaceReference.Absolute(root, entry.Path);
        Add("workbench.copy-path", "common.copy_path", () => Copy(absolute), absolute.Length > 0);
        if (engine?.IsRemote != true)
        {
            menu.Items.Add(new Separator());
            Add("workbench.reveal", OperatingSystem.IsMacOS() ? "workbench.reveal_in_finder" : OperatingSystem.IsWindows() ? "workbench.reveal_in_file_explorer" : "workbench.show_in_file_manager", async () =>
            {
                if (await new Host.AvaloniaHost(this).RevealPathAsync(root, entry.Path).ConfigureAwait(true) is { } refusal)
                {
                    throw new InvalidOperationException(refusal.Error);
                }
            });
            if (!entry.Directory)
            {
                Add("workbench.open-file", "workbench.open_with_default_application", async () =>
                {
                    if (await new Host.AvaloniaHost(this).OpenFileAsync(root, entry.Path).ConfigureAwait(true) is { } refusal)
                    {
                        throw new InvalidOperationException(refusal.Error);
                    }
                });
            }
        }

        return menu;

        async Task Copy(string text)
        {
            if (Clipboard is null)
            {
                throw new InvalidOperationException(MessageCatalog.Get("common.copy_failed_try_again", Language));
            }

            await Clipboard.SetTextAsync(text).ConfigureAwait(true);
        }

        void Add(string action, string key, Func<Task> perform, bool enabled = true)
        {
            var item = new MenuItem { Header = MessageCatalog.Get(key, Language), IsEnabled = enabled };
            if (this.TryFindResource("WorkspaceMenuItemTheme", out var theme))
            {
                item.Theme = theme as Avalonia.Styling.ControlTheme;
            }

            AutomationProperties.SetAutomationId(item, "action:" + action);
            item.Click += async (_, _) =>
            {
                item.IsEnabled = false;
                try
                {
                    await perform().ConfigureAwait(true);
                }
                catch (Exception error)
                {
                    ReportWorkbenchError(error);
                }
                finally
                {
                    item.IsEnabled = enabled;
                }
            };
            menu.Items.Add(item);
        }
    }

    /// <summary>Projects file language, caret line and unsaved state without replacing the editor.</summary>
    private void DrawFileStatus()
    {
        FileStatus.IsVisible = _openFile is not null && FileEditor.IsVisible && !WorkbenchStart.IsVisible && !TerminalSurface.IsVisible;
        if (_openFile is not { } file)
        {
            return;
        }

        var extension = Path.GetExtension(file.Original.Path).TrimStart('.').ToLowerInvariant();
        FileLanguage.Text = WorkspaceReference.Language(extension);
        FileLine.Text = MessageCatalog.Get("workbench.line_n", Language, ("n", FileEditor.CurrentLine.ToString(System.Globalization.CultureInfo.CurrentCulture)));
        FileDirty.Text = MessageCatalog.Get("workbench.unsaved", Language);
        FileDirty.IsVisible = file.Dirty;
        FileTabs.Children.OfType<Workbench.WorkbenchHeaderTab>()
            .FirstOrDefault(tab => Equals(tab.Tag, file.Original.Path))?.ShowDirty(file.Dirty);
    }
}
