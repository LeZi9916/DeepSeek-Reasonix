using AvaloniaTerminal;
using Reasonix.Studio.Kernel.Client;
using System;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>Owns the selected terminal transport while its native surface is mounted.</summary>
internal sealed class WorkbenchTerminal
{
    /// <summary>The active view lifetime, independent of the kernel process.</summary>
    private CancellationTokenSource? _lifetime;

    /// <summary>Raised when the selected terminal reports an exit code.</summary>
    internal event Action<int?>? Exited;

    /// <summary>Raised when the active terminal transport fails.</summary>
    internal event Action<Exception>? Failed;

    /// <summary>Gets the terminal selected by the workbench.</summary>
    internal string? Id
    {
        get; private set;
    }

    /// <summary>Detaches the UI without stopping the kernel's terminal.</summary>
    internal void Detach()
    {
        var lifetime = _lifetime;
        _lifetime = null;
        Id = null;
        lifetime?.Cancel();
    }

    /// <summary>Feeds one terminal model and serializes its input.</summary>
    /// <param name="id">Kernel terminal identity.</param><param name="api">Owning runtime.</param>
    /// <param name="model">Native terminal model.</param><param name="token">Window lifetime.</param>
    /// <returns>The transport lifetime.</returns>
    internal async Task AttachAsync(string id, StudioPageClient api, TerminalControlModel model, CancellationToken token)
    {
        Detach();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        _lifetime = lifetime;
        Id = id;
        var input = Channel.CreateUnbounded<TerminalUpdate>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        model.UserInput += (_, args) => input.Writer.TryWrite(new TerminalUpdate(Encoding.UTF8.GetString(args.Data.Span), 0, 0));
        model.SizeChanged += (_, args) => input.Writer.TryWrite(new TerminalUpdate(null, args.Cols, args.Rows));
        var sending = SendTerminalInputAsync(id, api, input.Reader, lifetime);
        try
        {
            await foreach (var frame in api.TerminalOutputAsync(id, lifetime.Token).ConfigureAwait(true))
            {
                if (!ReferenceEquals(_lifetime, lifetime))
                {
                    break;
                }

                if (frame.Kind is "replay" or "out" && frame.Data is { Length: > 0 } data)
                {
                    model.Feed(Convert.FromBase64String(data));
                }
                else if (frame.Kind == "exit")
                {
                    Exited?.Invoke(frame.Code);
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (ReferenceEquals(_lifetime, lifetime))
            {
                Failed?.Invoke(error);
            }
        }
        finally
        {
            await lifetime.CancelAsync().ConfigureAwait(true);
            input.Writer.TryComplete();
            await sending.ConfigureAwait(true);
            if (ReferenceEquals(_lifetime, lifetime))
            {
                _lifetime = null;
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
            if (ReferenceEquals(_lifetime, lifetime))
            {
                Failed?.Invoke(error);
            }

            await lifetime.CancelAsync().ConfigureAwait(true);
        }
    }

    /// <summary>An input or viewport change in user-event order.</summary>
    /// <param name="Text">Typed text, or null for a resize.</param>
    /// <param name="Cols">New column count.</param>
    /// <param name="Rows">New row count.</param>
    private sealed record TerminalUpdate(string? Text, int Cols, int Rows);
}
