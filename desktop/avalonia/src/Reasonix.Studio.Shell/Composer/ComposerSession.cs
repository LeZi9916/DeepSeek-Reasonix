using Reasonix.Studio.Contracts.Queue;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.Pages.Composer;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Composer;

/// <summary>Owns the input state and writes for the conversation bound to the composer.</summary>
/// <param name="drafts">Storage for unsent text.</param>
internal sealed class ComposerSession(IDraftStorage drafts) : IAsyncDisposable
{
    /// <summary>Serializes submits until the outstanding write settles.</summary>
    private readonly SemaphoreSlim _sending = new(1, 1);

    /// <summary>The runtime holding the displayed queue.</summary>
    private SessionEngine? _engine;

    /// <summary>Guards queue publications against rebinds and out-of-order responses.</summary>
    private long _queueRead;

    /// <summary>Gets the current draft scope.</summary>
    internal string DraftKey
    {
        get; private set;
    } = string.Empty;

    /// <summary>Gets the acknowledged queue for the current runtime.</summary>
    internal QueueSnapshot? Queue
    {
        get; private set;
    }

    /// <summary>Gets the attachments for the draft being edited.</summary>
    internal List<AttachmentChip> Attachments
    {
        get;
    } = [];

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        _queueRead++;
        await _sending.WaitAsync().ConfigureAwait(true);
        _sending.Dispose();
    }

    /// <summary>Rebinds the input and invalidates any in-flight queue read.</summary>
    /// <param name="engine">Owning runtime.</param><param name="key">Canonical draft scope.</param>
    internal void Bind(SessionEngine engine, string key)
    {
        if (!ReferenceEquals(_engine, engine) || DraftKey != key)
        {
            _queueRead++;
            Queue = null;
            _engine = engine;
            DraftKey = key;
        }
    }

    /// <summary>Reads the current draft.</summary>
    /// <returns>Text held in local storage.</returns>
    internal string ReadDraft() => DraftStore.Read(drafts, DraftKey);

    /// <summary>Writes a draft in its canonical scope.</summary>
    /// <param name="key">Draft scope.</param><param name="text">Unsent text.</param>
    internal void WriteDraft(string key, string text) => DraftStore.Write(drafts, key, text);

    /// <summary>Claims the submit slot.</summary>
    /// <returns>Whether the operation may proceed.</returns>
    internal bool TryBeginSend() => _sending.Wait(0);

    /// <summary>Releases the submit slot after the UI has settled.</summary>
    internal void EndSend() => _sending.Release();

    /// <summary>Sends the accepted draft and clears its stored copy if still current.</summary>
    /// <param name="engine">Runtime selected when the action began.</param><param name="draft">Draft scope at submit.</param>
    /// <param name="line">Submitted text.</param><param name="expanded">Submitted text with attachment context.</param>
    /// <param name="steer">Whether to steer the running turn.</param><param name="token">Cancels the submit.</param>
    /// <returns>The acknowledged send.</returns>
    internal async Task SendAsync(SessionEngine engine, string draft, string line, string expanded, bool steer, CancellationToken token)
    {
        await engine.SendAsync(line, expanded, steer, token).ConfigureAwait(true);
        if (DraftStore.Read(drafts, draft) == line)
        {
            WriteDraft(draft, string.Empty);
        }
    }

    /// <summary>Refreshes the queue from the current runtime.</summary>
    /// <param name="engine">Runtime selected when the read began.</param><param name="token">Cancels the read.</param>
    /// <returns>Whether an acknowledged result belongs to the current composer.</returns>
    internal async Task<bool> ReadQueueAsync(SessionEngine engine, CancellationToken token)
    {
        var read = ++_queueRead;
        QueueSnapshot? result;
        try
        {
            result = await engine.QueueAsync(token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception)
        {
            result = null;
        }

        if (read != _queueRead || (_engine is not null && !ReferenceEquals(_engine, engine)))
        {
            return false;
        }

        Queue = result;
        return true;
    }
}
