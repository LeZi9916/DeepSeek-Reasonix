using System.Collections.Generic;

namespace Reasonix.Studio.State.Transcript;

/// <summary>
/// One turn's rows, split into the person's line, the work between, and the rows
/// after the last answer.
/// </summary>
/// <param name="Lead">The person's line that opened the turn, or null before the first one.</param>
/// <param name="Work">The rows between the line and the turn's last answer.</param>
/// <param name="Tail">The last answer and everything after it, which is what a reader came for.</param>
/// <remarks>
/// The split is what lets a turn's intermediate work fold under one line while the
/// answer stays on screen. It is decided here rather than by the page because it
/// is a rule about rows, and a page that split them itself would be a second
/// answer to the same question.
/// </remarks>
public sealed record TurnSegment(UserItem? Lead, IReadOnlyList<TranscriptItem> Work, IReadOnlyList<TranscriptItem> Tail)
{
    /// <summary>
    /// Gets a value indicating whether this turn has work worth folding.
    /// </summary>
    public bool HasWork => Work.Count > 0;

    /// <summary>
    /// Splits rows into turns at each line the person sent.
    /// </summary>
    /// <param name="items">The rows, in transcript order.</param>
    /// <returns>One segment per turn, in order.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="items"/> is null.</exception>
    /// <remarks>
    /// A turn ends at the next line the person sent, not at the answer: a turn can
    /// hold several answers around its calls, and folding only the calls would
    /// leave a reader scrolling through the half of it they did not ask for.
    /// </remarks>
    public static IReadOnlyList<TurnSegment> Split(IReadOnlyList<TranscriptItem> items)
    {
        System.ArgumentNullException.ThrowIfNull(items);

        var segments = new List<TurnSegment>();
        var at = 0;
        while (at < items.Count)
        {
            var lead = items[at] as UserItem;
            var start = lead is null ? at : at + 1;

            var end = start;
            while (end < items.Count && items[end] is not UserItem)
            {
                end += 1;
            }

            var tailAt = -1;
            for (var i = end - 1; i >= start; i--)
            {
                if (items[i] is SayItem)
                {
                    tailAt = i;
                    break;
                }
            }

            var work = new List<TranscriptItem>();
            var tail = new List<TranscriptItem>();
            for (var i = start; i < end; i++)
            {
                if (tailAt >= 0 && i >= tailAt)
                {
                    tail.Add(items[i]);
                }
                else
                {
                    work.Add(items[i]);
                }
            }

            segments.Add(new TurnSegment(lead, work, tail));
            at = end;
        }

        return segments;
    }
}
