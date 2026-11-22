using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaTerminal;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The currently attached terminal view; the PTY itself belongs to the kernel.</summary>
    private CancellationTokenSource? _terminalView;

    /// <summary>The PTY currently shown or awaiting explicit close.</summary>
    private string? _terminalId;

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
        if (_engine is null || _terminalId is not { } id)
        {
            return;
        }

        var engine = _engine;

        var menu = new MenuFlyout();
        var confirm = new MenuItem { Header = MessageCatalog.Get(MessageKey.Terminal.CloseAndEnd, Language) };
        confirm.Click += async (_, _) =>
        {
            if (ReferenceEquals(_engine, engine) && _terminalId == id && await TryAnswerAsync(() => engine.Pages.CloseTerminalAsync(id, _closing.Token)).ConfigureAwait(true) && ReferenceEquals(_engine, engine) && _terminalId == id)
            {
                ReleasePreview();
                FileEditor.IsVisible = true;
                WorkbenchTitle.Text = MessageCatalog.Get(MessageKey.Common.Workbench, Language);
            }
        };
        menu.Items.Add(confirm);
        menu.ShowAt(EndTerminal);
    }

    /// <summary>Detaches the view without ending the user's kernel-owned process.</summary>
    private void DetachTerminal()
    {
        _terminalView?.Cancel();
        _terminalView = null;
        _terminalId = null;
        EndTerminal.IsVisible = false;
        TerminalSurface.IsVisible = false;
        TerminalSurface.Content = null;
    }

    /// <summary>Attaches a VT emulator to a single selected PTY.</summary>
    /// <param name="id">Kernel PTY id.</param>
    /// <param name="api">The owning session.</param>
    private async void AttachTerminal(string id, StudioPageClient api)
    {
        WorkbenchStart.IsVisible = false;
        DetachTerminal();
        HideBrowser();
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
        _terminalId = id;
        EndTerminal.IsVisible = true;
        WorkbenchTitle.Text = MessageCatalog.Get(MessageKey.Workbench.Terminal, Language);
        terminal.Focus();

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
        _terminalView = lifetime;
        var token = lifetime.Token;
        var input = Channel.CreateUnbounded<TerminalUpdate>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        model.UserInput += (_, args) => input.Writer.TryWrite(new TerminalUpdate(Encoding.UTF8.GetString(args.Data.Span), 0, 0));
        model.SizeChanged += (_, args) => input.Writer.TryWrite(new TerminalUpdate(null, args.Cols, args.Rows));
        var sending = SendTerminalInputAsync(id, api, input.Reader, lifetime);
        try
        {
            await foreach (var frame in api.TerminalOutputAsync(id, token).ConfigureAwait(true))
            {
                if (frame.Kind is "replay" or "out" && frame.Data is { Length: > 0 } data)
                {
                    model.Feed(Convert.FromBase64String(data));
                }
                else if (frame.Kind == "exit")
                {
                    WorkbenchTitle.Text += " · " + frame.Code;
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            ReportWorkbenchError(error);
        }
        finally
        {
            await lifetime.CancelAsync().ConfigureAwait(true);
            input.Writer.TryComplete();
            await sending.ConfigureAwait(true);
            if (_terminalView == lifetime)
            {
                _terminalView = null;
            }
        }
    }

    /// <summary>Preserves keystroke ordering even across slow HTTP acknowledgements.</summary>
    /// <param name="id">Terminal id.</param>
    /// <param name="api">Owning session.</param>
    /// <param name="input">Emulator input queue.</param>
    /// <param name="lifetime">The active view lifetime.</param>
    /// <returns>The input pump.</returns>
    private async Task SendTerminalInputAsync(string id, StudioPageClient api, ChannelReader<TerminalUpdate> input, CancellationTokenSource lifetime)
    {
        try
        {
            await foreach (var update in input.ReadAllAsync(lifetime.Token).ConfigureAwait(true))
            {
                if (update.Text is { } text)
                {
                    await api.TerminalInputAsync(id, text, lifetime.Token).ConfigureAwait(true);
                }
                else
                {
                    await api.TerminalSizeAsync(id, update.Cols, update.Rows, lifetime.Token).ConfigureAwait(true);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            ReportWorkbenchError(error);
            await lifetime.CancelAsync().ConfigureAwait(true);
        }
    }

    /// <summary>An input or viewport change in user-event order.</summary>
    /// <param name="Text">Typed text, or null for a resize.</param>
    /// <param name="Cols">New column count.</param>
    /// <param name="Rows">New row count.</param>
    private sealed record TerminalUpdate(string? Text, int Cols, int Rows);
}
