using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaTerminal;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Workbench;
using System;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Owns the terminal stream attached to the workbench.</summary>
    private readonly WorkbenchTerminal _terminal = new();

    /// <summary>Shows live PTYs and offers to start a shell only on an explicit selection.</summary>
    /// <param name="sender">The terminal navigation item.</param>
    /// <param name="e">The click.</param>
    private async void OnTerminals(object? sender, RoutedEventArgs e)
    {
        if (_engine is null)
        {
            return;
        }

        var engine = _engine;
        var api = engine.Pages;
        await TryAnswerAsync(async () =>
        {
            var terminals = await api.TerminalsAsync(_closing.Token).ConfigureAwait(true);
            if (!ReferenceEquals(engine, _engine) || _closing.IsCancellationRequested)
            {
                return;
            }

            var menu = new MenuFlyout();
            var start = new MenuItem { Header = MessageCatalog.Get(MessageKey.Common.Open, Language) + " · " + MessageCatalog.Get(MessageKey.Workbench.Terminal, Language) };
            start.Click += async (_, _) =>
            {
                if (ReferenceEquals(engine, _engine))
                {
                    await TryAnswerAsync(async () =>
                    {
                        var id = await api.OpenTerminalAsync(_closing.Token).ConfigureAwait(true);
                        if (ReferenceEquals(engine, _engine))
                        {
                            AttachTerminal(id, api);
                        }
                    }).ConfigureAwait(true);
                }
            };
            menu.Items.Add(start);
            foreach (var entry in terminals)
            {
                if ((string?)entry?["id"] is not { } id)
                {
                    continue;
                }

                var item = new MenuItem { Header = (string?)entry?["name"] ?? id };
                item.Click += (_, _) =>
                {
                    if (ReferenceEquals(_engine, engine))
                    {
                        AttachTerminal(id, api);
                    }
                };
                menu.Items.Add(item);
            }

            menu.ShowAt(ViewSwitch);
        }).ConfigureAwait(true);
    }

    /// <summary>Asks for confirmation before ending a kernel-owned shell.</summary>
    /// <param name="sender">The terminal close button.</param>
    /// <param name="e">The click.</param>
    private void ConfirmTerminalClose(object? sender, RoutedEventArgs e)
    {
        if (_engine is null || _terminal.Id is not { } id)
        {
            return;
        }

        var engine = _engine;

        var menu = new MenuFlyout();
        var confirm = new MenuItem { Header = MessageCatalog.Get(MessageKey.Terminal.CloseAndEnd, Language) };
        confirm.Click += async (_, _) =>
        {
            if (ReferenceEquals(_engine, engine) && _terminal.Id == id && await TryAnswerAsync(() => engine.Pages.CloseTerminalAsync(id, _closing.Token)).ConfigureAwait(true) && ReferenceEquals(_engine, engine) && _terminal.Id == id)
            {
                ReleasePreview();
                FileEditor.IsVisible = true;
                SetFileBarTitle(MessageCatalog.Get(MessageKey.Common.Workbench, Language), path: false);
            }
        };
        menu.Items.Add(confirm);
        menu.ShowAt(EndTerminal);
    }

    /// <summary>Detaches the view without ending the user's kernel-owned process.</summary>
    private void DetachTerminal()
    {
        _terminal.Detach();
        EndTerminal.IsVisible = false;
        TerminalSurface.IsVisible = false;
        TerminalSurface.Content = null;
    }

    /// <summary>Attaches a VT emulator to a single selected PTY.</summary>
    /// <param name="id">Kernel PTY id.</param>
    /// <param name="api">The owning session.</param>
    private async void AttachTerminal(string id, StudioPageClient api)
    {
        SetWorkbenchLauncher(false);
        DetachTerminal();
        HideBrowser();
        ExplorerPane.IsVisible = false;
        _fileRead++;
        _openFile = null;
        ShowAnalysis(false);
        WorkbenchPanel.IsVisible = true;
        LayoutWorkbench();
        var model = new TerminalControlModel();
        var terminal = new TerminalControl { Model = model };
        ReleasePreview();
        FileEditor.IsVisible = false;
        SaveFile.IsEnabled = false;
        PreviewFile.IsVisible = false;
        TerminalSurface.Content = terminal;
        TerminalSurface.IsVisible = true;
        EndTerminal.IsVisible = true;
        SetFileBarTitle(MessageCatalog.Get(MessageKey.Workbench.Terminal, Language), path: false);
        terminal.Focus();

        await _terminal.AttachAsync(id, api, model, _closing.Token).ConfigureAwait(true);
    }
}
