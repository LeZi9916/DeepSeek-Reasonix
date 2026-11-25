using Avalonia.Threading;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Session;

internal sealed partial class WindowSessionCoordinator
{
    /// <summary>Cancels stream slots and polling when the coordinator is disposed.</summary>
    private readonly CancellationTokenSource _streamsClosing = new();

    /// <summary>Refreshes metadata for retained panes when their stream is paused.</summary>
    private readonly DispatcherTimer _parkedPaneReads = new() { Interval = TimeSpan.FromSeconds(5) };

    /// <summary>Prevents overlapping status polling passes.</summary>
    private bool _pollingParkedPanes;

    /// <summary>Raised when the focused stream fails.</summary>
    internal event Action<Exception>? StreamFailed;

    /// <summary>Gets or sets the callback to update a parked pane's canonical status.</summary>
    internal Func<SessionPaneState, Task>? RefreshParkedStatus
    {
        get; set;
    }

    /// <summary>Gets or sets the callback to update a parked pane's history.</summary>
    internal Func<SessionPaneState, Task>? RefreshParkedHistory
    {
        get; set;
    }

    /// <summary>Gets or sets the preferred number of simultaneous conversation watches.</summary>
    internal int StreamLimit
    {
        get; set;
    } = 3;

    /// <summary>Connects the polling clock to the window's lifetime.</summary>
    internal void StartStreamScheduling()
    {
        _parkedPaneReads.Tick += async (_, _) => await ReadParkedPanesAsync().ConfigureAwait(true);
    }

    /// <summary>Claims a stream slot for an observed pane and binds its engine.</summary>
    /// <param name="pane">The observed pane.</param>
    internal void EnsureWatching(SessionPaneState pane)
    {
        if (ReferenceEquals(Focused, pane))
        {
            pane.WatchedAt = DateTimeOffset.UtcNow;
        }

        if (!pane.WatchBound)
        {
            pane.WatchBound = true;
            Bind(pane.Engine);
            _ = pane.Engine.EnsureTreeWatching();
        }

        ReconcilePaneStreams();
    }

    /// <summary>Gives live streams first to the selected pane, then running panes, then recent panes.</summary>
    internal void ReconcilePaneStreams()
    {
        if (_streamsClosing.IsCancellationRequested)
        {
            return;
        }

        var panes = _panes.Where(pane => pane.WatchBound).ToArray();
        var limit = StreamLimit is >= 1 and <= 4 ? StreamLimit : 3;
        var preferred = panes
            .OrderByDescending(pane => ReferenceEquals(Focused, pane))
            .ThenByDescending(pane => pane.Engine.State.Running || pane.Status.Running)
            .ThenByDescending(pane => pane.WatchedAt)
            .Take(limit)
            .ToArray();

        foreach (var pane in panes.Where(pane => !preferred.Contains(pane)))
        {
            pane.StreamCancellation?.Cancel();
        }

        var occupied = panes.Count(pane => pane.StreamCancellation is not null);
        foreach (var pane in preferred.Where(pane => pane.StreamCancellation is null).Take(Math.Max(0, limit - occupied)))
        {
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_streamsClosing.Token);
            pane.StreamCancellation = lifetime;
            pane.Watching = WatchPaneAsync(pane, lifetime);
        }

        if (panes.Any(pane => !preferred.Contains(pane)))
        {
            _parkedPaneReads.Start();
        }
        else
        {
            _parkedPaneReads.Stop();
        }
    }

    /// <summary>Watches one runtime until its stream slot is released.</summary>
    /// <param name="pane">The owning runtime.</param>
    /// <param name="lifetime">This particular streaming period.</param>
    /// <returns>The active stream lifetime.</returns>
    private async Task WatchPaneAsync(SessionPaneState pane, CancellationTokenSource lifetime)
    {
        await Task.Yield();
        try
        {
            await pane.Engine.WatchConversationAsync(lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!_streamsClosing.IsCancellationRequested && ReferenceEquals(Focused, pane))
            {
                StreamFailed?.Invoke(error);
            }
        }
        finally
        {
            var released = lifetime.IsCancellationRequested;
            pane.StreamCancellation = null;
            pane.Watching = null;
            lifetime.Dispose();
            if (released && !_streamsClosing.IsCancellationRequested)
            {
                ReconcilePaneStreams();
            }
        }
    }

    /// <summary>Checks the status of a parked pane and refreshes history when its turn settles.</summary>
    /// <returns>One metadata pass over paused streams.</returns>
    private async Task ReadParkedPanesAsync()
    {
        if (_pollingParkedPanes || _streamsClosing.IsCancellationRequested)
        {
            return;
        }

        _pollingParkedPanes = true;
        try
        {
            foreach (var pane in _panes.Where(pane => pane.WatchBound && pane.StreamCancellation is null).ToArray())
            {
                if (pane.StreamCancellation is not null || !_panes.Contains(pane))
                {
                    continue;
                }

                var running = pane.Engine.State.Running || pane.Status.Running;
                if (RefreshParkedStatus is { } status)
                {
                    await status(pane).ConfigureAwait(true);
                }

                if (running && !pane.Status.Running && !pane.Engine.State.Running)
                {
                    pane.Unread = true;
                    if (RefreshParkedHistory is { } history)
                    {
                        await history(pane).ConfigureAwait(true);
                    }
                }
            }

            ReconcilePaneStreams();
        }
        finally
        {
            _pollingParkedPanes = false;
        }
    }
}
