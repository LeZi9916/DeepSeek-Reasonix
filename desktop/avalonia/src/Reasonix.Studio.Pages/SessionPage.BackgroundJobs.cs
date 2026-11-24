using Avalonia.Threading;
using Reasonix.Studio.Contracts.Jobs;
using System;
using System.Collections.Generic;

namespace Reasonix.Studio.Pages;

public sealed partial class SessionPage
{
    /// <summary>Live and ended background jobs reported by the active conversation.</summary>
    private readonly Dictionary<string, BackgroundJobStatus> _backgroundJobs = new(StringComparer.Ordinal);

    /// <summary>Mounted tool cards linked to an acknowledged background job.</summary>
    private readonly List<ToolCallHeader> _backgroundTools = [];

    /// <summary>Updates running durations without redrawing any transcript rows.</summary>
    private readonly DispatcherTimer _backgroundClock = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>Raised when the reader opens a job from its originating tool card.</summary>
    public event Action<BackgroundJobStatus>? BackgroundJobOutputRequested;

    /// <summary>Updates the tool cards from the same background jobs shown in the meter rail.</summary>
    /// <param name="jobs">The active conversation's reported jobs.</param>
    public void ShowBackgroundJobs(IReadOnlyList<BackgroundJobStatus> jobs)
    {
        _backgroundJobs.Clear();
        foreach (var job in jobs)
        {
            _backgroundJobs[job.Id] = job;
        }

        RefreshBackgroundTools();
    }

    /// <summary>Joins mounted tool calls to the authoritative job list.</summary>
    private void RefreshBackgroundTools()
    {
        var running = false;
        foreach (var tool in _backgroundTools)
        {
            var job = tool.BackgroundJobId is { } id ? _backgroundJobs.GetValueOrDefault(id) : null;
            tool.ShowBackgroundJob(job);
            running |= job?.Status == "running";
        }

        if (running)
        {
            _backgroundClock.Start();
        }
        else
        {
            _backgroundClock.Stop();
        }
    }

    /// <summary>Resolves the latest job status when a visible tool link is activated.</summary>
    /// <param name="id">The job identifier reported on the tool result.</param>
    private void OpenBackgroundToolOutput(string id)
    {
        if (_backgroundJobs.TryGetValue(id, out var job) && job.Status == "running")
        {
            BackgroundJobOutputRequested?.Invoke(job);
        }
    }
}
