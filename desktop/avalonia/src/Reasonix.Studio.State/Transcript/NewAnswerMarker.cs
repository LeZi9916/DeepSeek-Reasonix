using System.Collections.Generic;

namespace Reasonix.Studio.State.Transcript;

/// <summary>
/// Decides where the line marking answers the reader has not seen belongs.
/// </summary>
/// <remarks>
/// The kernel keeps the unread mark; it does not keep where the reader stopped
/// reading. This window remembers the last row it was showing when the mark
/// cleared, and this rule turns that into a position: the line goes before the
/// first answer after it, and nowhere at all when nothing newer arrived.
/// </remarks>
public static class NewAnswerMarker
{
    /// <summary>
    /// Finds the row the line belongs before.
    /// </summary>
    /// <param name="items">The rows, in transcript order.</param>
    /// <param name="seenThrough">
    /// The last row identity the reader had seen, or null when nothing is known
    /// and the line has no place to go.
    /// </param>
    /// <returns>
    /// The index of the first answer after <paramref name="seenThrough"/>, or -1
    /// when the marker is owed nowhere.
    /// </returns>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="items"/> is null.</exception>
    /// <remarks>
    /// Only an answer starts the span: a following tool call or notice is part of
    /// the same reply, and a line drawn between them would claim the reader had
    /// seen half of it.
    /// </remarks>
    public static int PlaceOf(IReadOnlyList<TranscriptItem> items, string? seenThrough)
    {
        System.ArgumentNullException.ThrowIfNull(items);
        if (string.IsNullOrEmpty(seenThrough))
        {
            return -1;
        }

        var seen = false;
        for (var i = 0; i < items.Count; i++)
        {
            if (seen && items[i] is SayItem)
            {
                return i;
            }

            if (items[i].Id == seenThrough)
            {
                seen = true;
            }
        }

        return -1;
    }
}
