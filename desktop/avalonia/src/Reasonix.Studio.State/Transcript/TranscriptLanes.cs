using Reasonix.Studio.Contracts.Wire;
using System.Collections.Generic;

namespace Reasonix.Studio.State.Transcript;

/// <summary>
/// Splits a stretch of rendered rows into the groups the transcript draws.
/// </summary>
/// <remarks>
/// Two or more delegates sent out side by side read as one group of their own;
/// everything else is the ordinary run of rows. The split is a rule about the
/// rows, so it is decided here rather than by the page that draws them — a page
/// that split them itself would be a second answer to "what is a group".
/// </remarks>
public static class TranscriptLanes
{
    /// <summary>
    /// Reads whether a call was delegated to a sub-agent.
    /// </summary>
    /// <param name="call">The call to read.</param>
    /// <returns>True when the kernel dispatched it with a profile.</returns>
    /// <remarks>
    /// The profile is the signal rather than the tool's name: every dispatcher is
    /// reached through the same proxy, so the name says nothing about whether a
    /// separate context was opened. A child call of a delegate carries a parent
    /// id, and is a step inside the delegate rather than a delegate of its own.
    /// </remarks>
    public static bool IsDelegation(Tool call)
    {
        return call.Profile is not null && string.IsNullOrEmpty(call.ParentID);
    }

    /// <summary>
    /// Splits rows into lanes, two or more consecutive delegates taking a lane of
    /// their own and every other row joining the surrounding run.
    /// </summary>
    /// <param name="items">The rows to split, in transcript order.</param>
    /// <returns>The lanes, in order; a lane with delegates holds only them.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="items"/> is null.</exception>
    /// <remarks>
    /// A lone delegate stays in the ordinary run: one sub-agent is a call, and
    /// giving it a group of its own would frame every delegation as a fleet.
    /// </remarks>
    public static IReadOnlyList<TranscriptLane> Split(IReadOnlyList<TranscriptItem> items)
    {
        System.ArgumentNullException.ThrowIfNull(items);

        var lanes = new List<TranscriptLane>();
        var at = 0;
        while (at < items.Count)
        {
            var end = at;
            while (end < items.Count && items[end] is ToolItem tool && IsDelegation(tool.Call))
            {
                end += 1;
            }

            if (end - at >= 2)
            {
                lanes.Add(new TranscriptLane(Delegates(items, at, end), System.Array.Empty<TranscriptItem>()));
                at = end;
                continue;
            }

            // Either this row is not a delegate, or it is the only delegate in
            // its run; both cases take one row into the ordinary run, which is
            // where the loop's next pass continues from.
            var take = 1;
            var ordinary = new List<TranscriptItem>(take);
            for (var i = at; i < at + take; i++)
            {
                ordinary.Add(items[i]);
            }

            var last = lanes.Count > 0 ? lanes[lanes.Count - 1] : null;
            if (last is { Delegates.Count: 0 } run)
            {
                var joined = new List<TranscriptItem>(run.Rows);
                joined.AddRange(ordinary);
                lanes[lanes.Count - 1] = new TranscriptLane(System.Array.Empty<ToolItem>(), joined);
            }
            else
            {
                lanes.Add(new TranscriptLane(System.Array.Empty<ToolItem>(), ordinary));
            }

            at += take;
        }

        return lanes;
    }

    /// <summary>
    /// Copies the delegate rows of one run into a list.
    /// </summary>
    /// <param name="items">The rows.</param>
    /// <param name="from">The first index to take.</param>
    /// <param name="to">One past the last index to take.</param>
    /// <returns>The delegates in that range, in order.</returns>
    private static IReadOnlyList<ToolItem> Delegates(IReadOnlyList<TranscriptItem> items, int from, int to)
    {
        var delegates = new List<ToolItem>(to - from);
        for (var i = from; i < to; i++)
        {
            if (items[i] is ToolItem tool)
            {
                delegates.Add(tool);
            }
        }

        return delegates;
    }
}
