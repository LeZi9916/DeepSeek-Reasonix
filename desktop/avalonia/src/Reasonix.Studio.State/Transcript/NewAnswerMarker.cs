using System.Collections.Generic;

namespace Reasonix.Studio.State.Transcript;

/// <summary>
/// Finds the transcript row where a newly viewed unread span begins.
/// </summary>
/// <remarks>
/// The tree reports the last viewed timestamp while a row is unread. User
/// turns have timestamps; assistant rows without timestamps belong to the
/// most recent authored turn.
/// </remarks>
public static class NewAnswerMarker
{
    /// <summary>
    /// Finds the row the line belongs before.
    /// </summary>
    /// <param name="items">The rows, in transcript order.</param>
    /// <param name="viewedAt">The host's last-viewed timestamp in unix milliseconds.</param>
    /// <returns>The first new turn or response, or -1 when no divider is owed.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="items"/> is null.</exception>
    public static int PlaceOf(IReadOnlyList<TranscriptItem> items, long? viewedAt)
    {
        System.ArgumentNullException.ThrowIfNull(items);
        if (viewedAt is not > 0)
        {
            return -1;
        }

        var lastTurn = -1;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is UserItem { Pending: false, Steer: false } user)
            {
                if (user.At > viewedAt)
                {
                    return i == 0 ? -1 : i;
                }

                lastTurn = i;
            }
        }

        for (var i = lastTurn + 1; i < items.Count; i++)
        {
            if (items[i] is SayItem { HasContent: true })
            {
                return i == 0 ? -1 : i;
            }
        }

        return -1;
    }
}
