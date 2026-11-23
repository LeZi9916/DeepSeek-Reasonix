using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Pages.Rail;

public sealed partial class ProjectRail
{
    /// <summary>Completion times used only by the running filter.</summary>
    private readonly Dictionary<string, DateTimeOffset> _finished = new(StringComparer.Ordinal);

    /// <summary>Refreshes the filter at the earliest retained row's expiration.</summary>
    private readonly DispatcherTimer _graceTimer = new();

    /// <summary>The duration chosen for completed sessions.</summary>
    private TimeSpan _liveGrace;

    /// <summary>Changes how long finished runs remain listed as running.</summary>
    /// <param name="minutes">The retention period, or zero.</param>
    public void KeepFinishedFor(int minutes)
    {
        var period = TimeSpan.FromMinutes(Math.Max(0, minutes));
        if (_liveGrace == period)
        {
            return;
        }

        _graceTimer.Tick -= ExpireFinished;
        _graceTimer.Tick += ExpireFinished;
        _liveGrace = period;
        ScheduleGrace();
        Rebuild();
    }

    /// <summary>Observes the edge from a live to a settled turn.</summary>
    /// <param name="key">The transcript's canonical key.</param>
    /// <param name="live">Whether this report is live.</param>
    private void NoteCompletion(string key, bool live)
    {
        if (live)
        {
            _finished.Remove(key);
        }
        else if (_runs.GetValueOrDefault(key)?.Live == true)
        {
            _finished[key] = DateTimeOffset.UtcNow;
        }

        ScheduleGrace();
    }

    /// <summary>Includes a finished row only when its grace period is still in effect.</summary>
    /// <param name="host">The row's machine identity.</param>
    /// <param name="path">The canonical transcript.</param>
    /// <returns>Whether this row belongs in the running filter.</returns>
    private bool ListedAsRunning(string host, string path)
    {
        var key = RemoteKey(host, path);
        return _runs.GetValueOrDefault(key)?.Live == true
            || (_liveGrace > TimeSpan.Zero && _finished.TryGetValue(key, out var end) && DateTimeOffset.UtcNow - end < _liveGrace);
    }

    /// <summary>Schedules the filter's next automatic refresh.</summary>
    private void ScheduleGrace()
    {
        _graceTimer.Stop();
        var now = DateTimeOffset.UtcNow;
        foreach (var key in _finished.Where(row => now - row.Value >= TimeSpan.FromHours(4)).Select(row => row.Key).ToArray())
        {
            _finished.Remove(key);
        }

        if (_liveGrace == TimeSpan.Zero || _finished.Count == 0)
        {
            return;
        }

        var next = _finished.Values.Select(at => at + _liveGrace - now).Where(time => time > TimeSpan.Zero).DefaultIfEmpty().Min();
        if (next > TimeSpan.Zero)
        {
            _graceTimer.Interval = next + TimeSpan.FromMilliseconds(30);
            _graceTimer.Start();
        }
    }

    /// <summary>Refreshes the list when at least one completed row leaves the grace period.</summary>
    /// <param name="sender">The filter timer.</param>
    /// <param name="args">Timer notification.</param>
    private void ExpireFinished(object? sender, EventArgs args)
    {
        ScheduleGrace();
        Rebuild();
    }
}
