using Reasonix.Studio.Contracts.Diagnostics;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Session;

public sealed partial class SessionEngine
{
    /// <summary>The current read generation and retained canonical page boundaries.</summary>
    private readonly HistoryRead _historyRead = new();

    /// <summary>Raised when history pagination or its retry state changes.</summary>
    public event Action? HistoryChanged;

    /// <summary>Gets the completeness and activity of the mounted history window.</summary>
    public HistoryReadState History { get; private set; } = new();

    /// <summary>Reads the newest transcript page without replacing a failed read with an empty page.</summary>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The acknowledged transcript window.</returns>
    public Task LoadHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        lock (_historyRead)
        {
            if (_historyRead.Latest is { IsCompleted: false } pending)
            {
                return pending.WaitAsync(cancellationToken);
            }

            var ticket = _historyRead.NextGeneration();
            _historyRead.More = null;
            var lifetime = _closing.Token;
            _historyRead.Latest = Task.Run(() => ReadLatestAsync(ticket, lifetime));
            SetHistory(History with
            {
                Busy = true,
                Failed = false,
            });
            return _historyRead.Latest.WaitAsync(cancellationToken);
        }
    }

    /// <summary>Prepends an earlier page while retaining the current transcript on failure.</summary>
    /// <param name="token">Cancels reading.</param>
    /// <returns>The acknowledged preceding window.</returns>
    public Task LoadEarlierHistoryAsync(CancellationToken token = default)
    {
        lock (_historyRead)
        {
            if (_historyRead.Latest is { IsCompleted: false } latest)
            {
                return latest.WaitAsync(token);
            }

            if (_historyRead.More is { IsCompleted: false } pending)
            {
                return pending;
            }

            if (!History.HasMore || string.IsNullOrEmpty(_historyRead.Cursor))
            {
                return Task.CompletedTask;
            }

            var generation = _historyRead.Generation;
            var before = History.Earlier;
            var cursor = _historyRead.Cursor;
            var snapshot = _historyRead.Snapshot;
            var total = _historyRead.Total;
            _historyRead.More = Task.Run(() => ReadEarlierAsync(generation, before, cursor, snapshot, total, token));
            SetHistory(History with
            {
                Busy = true,
                Failed = false,
            });
            return _historyRead.More;
        }
    }

    /// <summary>Adopts the host's run state only while no newer frame has superseded the request.</summary>
    /// <param name="running">Canonical run state.</param><param name="revision">Stream revision when the request began.</param>
    public void RestoreRunning(bool running, long revision)
    {
        SessionState next;
        lock (_sync)
        {
            if (Generation.Revision != revision)
            {
                return;
            }

            if (_state.Running == running && (running || (_state.ResponseWait is null && !_state.Items.Any(item => item is SayItem { Done: false }))))
            {
                return;
            }

            next = _state with
            {
                Running = running,
                ResponseWait = running ? _state.ResponseWait : null,
                Items = running ? _state.Items : _state.Items.Select(item => item is SayItem { Done: false } say ? say with { Done = true } : item).ToArray(),
                Revision = _state.Revision + 1,
            };
            _state = next;
        }

        Changed?.Invoke(next);
    }

    /// <summary>Validates the snapshot and forward progress asserted by the kernel.</summary>
    /// <param name="page">The received history window.</param>
    private static void ValidatePage(HistoryPage page)
    {
        if (page.Snapshot.Length == 0 || page.Start < 0 || page.End < page.Start || page.Total < page.End || page.HasMoreBefore != (page.Start > 0) || (page.HasMoreBefore && string.IsNullOrEmpty(page.NextCursor)))
        {
            throw new InvalidDataException("Invalid history page.");
        }
    }

    /// <summary>Reads and projects one history window on a worker.</summary>
    /// <param name="ticket">The transcript read generation.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The acknowledged history read.</returns>
    private async Task ReadLatestAsync(long ticket, CancellationToken cancellationToken)
    {
        try
        {
            var page = await _client.HistoryPageAsync(null, cancellationToken).ConfigureAwait(false);
            var messages = page?.Messages ?? await _client.HistoryAsync(cancellationToken).ConfigureAwait(false);
            if (page is not null)
            {
                ValidatePage(page);
            }

            var started = StudioPerformance.Timestamp();
            var rows = HistoryProjection.Restore(messages, page?.ToolContext ?? []);
            StudioPerformance.Record("history.project", started);
            lock (_historyRead)
            {
                if (ticket != _historyRead.Generation)
                {
                    return;
                }

                _historyRead.Snapshot = page?.Snapshot ?? string.Empty;
                _historyRead.Total = page?.Total ?? messages.Count;
                _historyRead.Cursor = page?.NextCursor;
                RestoreHistory(rows, prepend: false);
                SetHistory(new HistoryReadState { Initialized = true, HasMore = page?.HasMoreBefore ?? false, Earlier = page?.Start ?? 0 });
            }

            _ = RefreshPlanAsync(cancellationToken);
        }
        catch (Exception error)
        {
            FailHistory(ticket, error);
            throw;
        }
    }

    /// <summary>Reads an earlier window for the same immutable snapshot.</summary>
    /// <param name="generation">The transcript read generation.</param>
    /// <param name="before">Number of messages preceding the retained window.</param>
    /// <param name="cursor">Opaque cursor for the preceding window.</param>
    /// <param name="snapshot">Snapshot identity at the time of the request.</param>
    /// <param name="total">Message count at the time of the request.</param>
    /// <param name="token">Cancels reading.</param>
    /// <returns>The acknowledged preceding page.</returns>
    private async Task ReadEarlierAsync(long generation, int before, string cursor, string snapshot, int total, CancellationToken token)
    {
        try
        {
            HistoryPage? page;
            try
            {
                page = await _client.HistoryPageAsync(cursor, token).ConfigureAwait(false);
            }
            catch (KernelRefusal error) when (error.Code == "history.snapshot_changed")
            {
                await ReloadAfterSnapshotChangeAsync(generation, token).ConfigureAwait(false);
                return;
            }

            if (generation != _historyRead.Generation)
            {
                return;
            }

            if (page is null)
            {
                throw new InvalidDataException("History pagination disappeared during a read.");
            }

            ValidatePage(page);
            if (page.Snapshot != snapshot)
            {
                await ReloadAfterSnapshotChangeAsync(generation, token).ConfigureAwait(false);
                return;
            }

            if (page.End != before || page.Total != total || page.Start >= page.End)
            {
                throw new InvalidDataException("History page did not advance.");
            }

            var started = StudioPerformance.Timestamp();
            var earlier = HistoryProjection.Restore(page.Messages, page.ToolContext);
            StudioPerformance.Record("history.project", started);
            lock (_historyRead)
            {
                if (generation != _historyRead.Generation)
                {
                    return;
                }

                _historyRead.Cursor = page.NextCursor;
                RestoreHistory(earlier, prepend: true);
                SetHistory(new HistoryReadState { Initialized = true, HasMore = page.HasMoreBefore, Earlier = page.Start });
            }
        }
        catch (Exception error)
        {
            FailHistory(generation, error);
            throw;
        }
    }

    /// <summary>Loads a fresh head if the rejected cursor still belongs to the current transcript.</summary>
    /// <param name="generation">The rejected read generation.</param>
    /// <param name="token">Cancels the replacement read.</param>
    /// <returns>The replacement read, when needed.</returns>
    private Task ReloadAfterSnapshotChangeAsync(long generation, CancellationToken token)
    {
        lock (_historyRead)
        {
            return generation == _historyRead.Generation ? LoadHistoryAsync(token) : Task.CompletedTask;
        }
    }

    /// <summary>Clears activity for a read that still owns the history window.</summary>
    /// <param name="generation">The read generation.</param>
    /// <param name="error">The read failure or cancellation.</param>
    private void FailHistory(long generation, Exception error)
    {
        lock (_historyRead)
        {
            if (generation == _historyRead.Generation)
            {
                SetHistory(History with
                {
                    Busy = false,
                    Failed = error is not OperationCanceledException,
                });
            }
        }
    }

    /// <summary>Invalidates pending reads when the host binds a different transcript.</summary>
    private void ResetHistoryBinding()
    {
        lock (_historyRead)
        {
            _historyRead.NextGeneration();
            _historyRead.Latest = null;
            _historyRead.More = null;
            _historyRead.Cursor = null;
            SetHistory(new HistoryReadState());
        }
    }

    /// <summary>Restores retained canonical messages without losing live interaction rows.</summary>
    /// <param name="page">The rows projected from the newly fetched messages.</param>
    /// <param name="prepend">Whether the existing snapshot and all newer live rows must be preserved.</param>
    private void RestoreHistory(List<TranscriptItem> page, bool prepend)
    {
        var items = page;
        SessionState next;
        lock (_sync)
        {
            if (prepend)
            {
                var preceding = new Dictionary<string, int>(items.Count, StringComparer.Ordinal);
                for (var i = 0; i < items.Count; i++)
                {
                    preceding[items[i].Id] = i;
                }

                foreach (var current in _state.Items)
                {
                    if (preceding.TryGetValue(current.Id, out var prior))
                    {
                        // The later page already owns the tool result for the earlier dispatch.
                        items[prior] = current;
                    }
                    else
                    {
                        items.Add(current);
                    }
                }
            }
            else
            {
                var live = _state.Items.Where(i => i is UserItem { Pending: true } or ApprovalItem { Open: true } or AskItem { Answered: null, Outcome: null } || (_state.Running && i is SayItem { Done: false } or ToolItem { Running: true })).ToArray();
                var calls = live.OfType<ToolItem>().Where(item => item.Call.ID is not null).ToDictionary(item => item.Call.ID);
                var known = items.OfType<ToolItem>().Select(item => item.Call.ID).ToHashSet();
                var committed = items.LastOrDefault() is SayItem && !calls.Keys.Any(id => !known.Contains(id));
                items = items.Select(item => item is ToolItem tool && string.IsNullOrEmpty(tool.Call.Output) && string.IsNullOrEmpty(tool.Call.Err) && calls.TryGetValue(tool.Call.ID, out var running) ? running with { Id = tool.Id } : item).ToList();
                items.AddRange(live.Where(item => item is not SayItem || !committed).Where(item => item is not ToolItem tool || !known.Contains(tool.Call.ID)));
            }

            next = _state with
            {
                Items = items,
                EnteredThrough = items.Count,
                IsRestored = true,
                Revision = _state.Revision + 1,
            };
            _state = next;
        }

        Changed?.Invoke(next);
    }

    /// <summary>Publishes pagination activity and completeness to its consumers.</summary>
    /// <param name="state">The acknowledged history-read state.</param>
    private void SetHistory(HistoryReadState state)
    {
        History = state;
        HistoryChanged?.Invoke();
    }

    /// <summary>Owns one transcript's read generation and retained snapshot.</summary>
    private sealed class HistoryRead
    {
        /// <summary>Only the latest read generation may publish.</summary>
        private long _generation;

        /// <summary>Gets the current read generation atomically.</summary>
        internal long Generation => Interlocked.Read(ref _generation);

        /// <summary>Gets or sets the kernel's immutable snapshot identity.</summary>
        internal string Snapshot { get; set; } = string.Empty;

        /// <summary>Gets or sets the opaque preceding-page cursor.</summary>
        internal string? Cursor
        {
            get; set;
        }

        /// <summary>Gets or sets the snapshot's canonical message count.</summary>
        internal int Total
        {
            get; set;
        }

        /// <summary>Gets or sets the single outstanding newest-page read.</summary>
        internal Task? Latest
        {
            get; set;
        }

        /// <summary>Gets or sets the single outstanding preceding-page read.</summary>
        internal Task? More
        {
            get; set;
        }

        /// <summary>Advances the owner generation using the same atomic operation as reads.</summary>
        /// <returns>The new generation.</returns>
        internal long NextGeneration() => Interlocked.Increment(ref _generation);
    }
}
