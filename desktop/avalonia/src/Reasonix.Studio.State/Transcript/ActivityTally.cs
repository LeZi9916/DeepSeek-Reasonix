using System.Collections.Generic;

namespace Reasonix.Studio.State.Transcript;

/// <summary>
/// The work one stretch of a turn did, counted and timed.
/// </summary>
/// <remarks>
/// The head of an activity group says what the work came to without the reader
/// opening it: how many calls, how many are still running, how many failed, and
/// how long the stretch took. All four are read off the rows, so the number on
/// screen is the rows' own sum rather than a counter kept beside them.
/// </remarks>
/// <param name="Calls">How many calls the stretch holds, reads rows counted by their tools.</param>
/// <param name="Running">How many are still running.</param>
/// <param name="Failed">How many ended in an error.</param>
/// <param name="Milliseconds">How long the stretch took, as the kernel measured it.</param>
/// <param name="Settled">Whether every call has finished.</param>
public readonly record struct ActivityTally(int Calls, int Running, int Failed, long Milliseconds, bool Settled)
{
    /// <summary>
    /// Reads the tally of one stretch of rows.
    /// </summary>
    /// <param name="items">The rows the stretch holds.</param>
    /// <returns>The tally, counting only calls and their reads rows.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="items"/> is null.</exception>
    /// <remarks>
    /// The span is the earliest start to the latest end when every call says
    /// both, and the sum of their durations otherwise. Capping it at the sum
    /// would understate work that ran in parallel; taking the first start and the
    /// last end is what makes concurrent calls read as the wall time a person
    /// waited.
    /// </remarks>
    public static ActivityTally Of(IReadOnlyList<TranscriptItem> items)
    {
        System.ArgumentNullException.ThrowIfNull(items);

        var calls = new List<Contracts.Wire.Tool>();
        var running = 0;
        foreach (var item in items)
        {
            switch (item)
            {
                case ToolItem tool:
                    calls.Add(tool.Call);

                    // The row's own state, not a guess from its timestamps: a
                    // call carries no end time until the kernel reports one, and
                    // reading absence as "still running" would call every settled
                    // call that timed nothing a running one.
                    if (tool.Running)
                    {
                        running += 1;
                    }

                    break;

                case ReadsItem reads:
                    // A reads row is only ever made from settled calls.
                    calls.AddRange(reads.Tools);
                    break;
            }
        }

        var failed = 0;
        var started = long.MaxValue;
        var ended = 0L;
        var summed = 0L;
        var everyCallTimes = calls.Count > 0;

        foreach (var call in calls)
        {
            if (!string.IsNullOrEmpty(call.Err))
            {
                failed += 1;
            }

            summed += call.DurationMs;

            if (call.StartedAt > 0 && call.EndedAt >= call.StartedAt)
            {
                started = System.Math.Min(started, call.StartedAt);
                ended = System.Math.Max(ended, call.EndedAt);
            }
            else
            {
                everyCallTimes = false;
            }
        }

        var span = everyCallTimes && started != long.MaxValue
            ? ended - started
            : summed;

        return new ActivityTally(calls.Count, running, failed, System.Math.Max(0, span), running == 0);
    }
}
