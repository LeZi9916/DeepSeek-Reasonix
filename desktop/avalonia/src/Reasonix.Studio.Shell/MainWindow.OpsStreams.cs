using Avalonia.Threading;
using Reasonix.Studio.Shell.Session;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Refreshes metadata for retained panes when their stream is paused.</summary>
    private readonly DispatcherTimer _parkedPaneReads = new() { Interval = TimeSpan.FromSeconds(5) };

    /// <summary>Prevents overlapping status polling passes.</summary>
    private bool _pollingParkedPanes;

    /// <summary>Starts tracking stream capacity for the window.</summary>
    private void InitializePaneStreams()
    {
        _parkedPaneReads.Tick += async (_, _) => await ReadParkedPanesAsync().ConfigureAwait(true);
        Closed += (_, _) => _parkedPaneReads.Stop();
    }

    /// <summary>Gives live streams first to the selected pane, then running panes, then recent panes.</summary>
    private void ReconcilePaneStreams()
    {
        if (_navigation is null || _closing.IsCancellationRequested)
        {
            return;
        }

        var panes = _navigation.Panes.Where(pane => pane.WatchBound).ToArray();
        var limit = _workspacePreferences.LiveStreamLimit is >= 1 and <= 4 and var chosen ? chosen : 3;
        var preferred = panes
            .OrderByDescending(pane => ReferenceEquals(_activePane, pane))
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
            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
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
            if (!_closing.IsCancellationRequested && ReferenceEquals(_activePane, pane))
            {
                ShowRefusal(error);
            }
        }
        finally
        {
            var released = lifetime.IsCancellationRequested;
            pane.StreamCancellation = null;
            pane.Watching = null;
            lifetime.Dispose();
            if (released && !_closing.IsCancellationRequested)
            {
                ReconcilePaneStreams();
            }
        }
    }

    /// <summary>Checks the status of a parked pane and refreshes history when its turn settles.</summary>
    /// <returns>One metadata pass over paused streams.</returns>
    private async Task ReadParkedPanesAsync()
    {
        if (_pollingParkedPanes || _navigation is null || _closing.IsCancellationRequested)
        {
            return;
        }

        _pollingParkedPanes = true;
        try
        {
            foreach (var pane in _navigation.Panes.Where(pane => pane.WatchBound && pane.StreamCancellation is null).ToArray())
            {
                if (pane.StreamCancellation is not null || !_navigation.Panes.Contains(pane))
                {
                    continue;
                }

                var running = pane.Engine.State.Running || pane.Status.Running;
                await ReadPaneStatusAsync(pane).ConfigureAwait(true);
                if (running && !pane.Status.Running && !pane.Engine.State.Running)
                {
                    pane.Unread = true;
                    await ReadPaneHistoryAsync(pane, true).ConfigureAwait(true);
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
