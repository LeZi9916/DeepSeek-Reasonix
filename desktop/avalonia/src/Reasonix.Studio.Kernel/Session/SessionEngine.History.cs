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
    public async Task LoadHistoryAsync(CancellationToken cancellationToken = default)
    {
        var ticket = _historyRead.NextGeneration();
        SetHistory(History with
        {
            Busy = true,
            Failed = false,
        });
        try
        {
            var page = await _client.HistoryPageAsync(null, cancellationToken).ConfigureAwait(false);
            var messages = page?.Messages ?? await _client.HistoryAsync(cancellationToken).ConfigureAwait(false);
            if (ticket != _historyRead.Generation)
            {
                return;
            }

            if (page is not null)
            {
                ValidatePage(page);
            }

            _historyRead.Messages = messages.ToArray();
            _historyRead.Context = page?.ToolContext ?? [];
            _historyRead.Snapshot = page?.Snapshot ?? string.Empty;
            _historyRead.Total = page?.Total ?? messages.Count;
            _historyRead.Cursor = page?.NextCursor;
            RestoreHistory(false);
            SetHistory(new HistoryReadState { Initialized = true, HasMore = page?.HasMoreBefore ?? false, Earlier = page?.Start ?? 0 });
        }
        catch
        {
            if (ticket == _historyRead.Generation)
            {
                SetHistory(History with
                {
                    Busy = false,
                    Failed = true,
                });
            }

            throw;
        }
    }

    /// <summary>Prepends an earlier page while retaining the current transcript on failure.</summary>
    /// <param name="token">Cancels reading.</param>
    /// <returns>The acknowledged preceding window.</returns>
    public Task LoadEarlierHistoryAsync(CancellationToken token = default)
    {
        lock (_historyRead)
        {
            if (_historyRead.More is { IsCompleted: false } pending)
            {
                return pending;
            }

            if (!History.HasMore || string.IsNullOrEmpty(_historyRead.Cursor))
            {
                return Task.CompletedTask;
            }

            return _historyRead.More = ReadEarlierAsync(token);
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

            next = _state with
            {
                Running = running,
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

    /// <summary>Reads an earlier window for the same immutable snapshot.</summary>
    /// <param name="token">Cancels reading.</param><returns>The prepended history window.</returns>
    private async Task ReadEarlierAsync(CancellationToken token)
    {
        var generation = _historyRead.Generation;
        var before = History.Earlier;
        SetHistory(History with
        {
            Busy = true,
            Failed = false,
        });
        try
        {
            HistoryPage? page;
            try
            {
                page = await _client.HistoryPageAsync(_historyRead.Cursor, token).ConfigureAwait(false);
            }
            catch (KernelRefusal error) when (error.Code == "history.snapshot_changed")
            {
                await LoadHistoryAsync(token).ConfigureAwait(false);
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
            if (page.Snapshot != _historyRead.Snapshot)
            {
                await LoadHistoryAsync(token).ConfigureAwait(false);
                return;
            }

            if (page.End != before || page.Total != _historyRead.Total || page.Start >= page.End)
            {
                throw new InvalidDataException("History page did not advance.");
            }

            _historyRead.Messages = page.Messages.Concat(_historyRead.Messages).ToArray();
            _historyRead.Context = page.ToolContext.Concat(_historyRead.Context).DistinctBy(c => c.Call.Id).ToArray();
            _historyRead.Cursor = page.NextCursor;
            RestoreHistory(true);
            SetHistory(new HistoryReadState { Initialized = true, HasMore = page.HasMoreBefore, Earlier = page.Start });
        }
        catch
        {
            if (generation == _historyRead.Generation)
            {
                SetHistory(History with
                {
                    Busy = false,
                    Failed = true,
                });
            }

            throw;
        }
    }

    /// <summary>Restores retained canonical messages without losing live interaction rows.</summary>
    /// <param name="prepend">Whether the existing snapshot and all newer live rows must be preserved.</param>
    private void RestoreHistory(bool prepend)
    {
        var items = HistoryProjection.Restore(_historyRead.Messages, _historyRead.Context).ToList();
        SessionState next;
        lock (_sync)
        {
            if (prepend)
            {
                var current = _state.Items.ToDictionary(item => item.Id);
                var known = items.Select(item => item.Id).ToHashSet();
                items = items.Select(item => current.GetValueOrDefault(item.Id, item)).Concat(_state.Items.Where(item => !known.Contains(item.Id))).ToList();
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

        /// <summary>Gets or sets canonical messages retained across page reads.</summary>
        internal IReadOnlyList<HistoryMessage> Messages { get; set; } = [];

        /// <summary>Gets or sets dispatches retained across page boundaries.</summary>
        internal HistoryToolContext[] Context { get; set; } = [];

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
