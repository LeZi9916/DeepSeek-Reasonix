using System;
using System.Globalization;
using System.Threading;

namespace Reasonix.Studio.State;

/// <summary>
/// Mints the identity of every row the client adds.
/// </summary>
/// <remarks>
/// One sequence for every card, so two of them cannot collide: a caller that has
/// to name the row it just added — to take it back when the kernel refuses it, or
/// to join it to the turn the kernel names — draws from here. The number is also
/// how a projection tells which rows it has already let in, which is why it is
/// monotonic rather than a fresh guid each time.
/// </remarks>
public sealed class IdSequence
{
    /// <summary>
    /// The last number handed out.
    /// </summary>
    private long _next;

    /// <summary>
    /// Reads the sequence number a row identity was minted at.
    /// </summary>
    /// <param name="id">The row identity.</param>
    /// <returns>The number, or 0 for an identity this sequence did not mint.</returns>
    public static long NumberOf(string id)
    {
        if (id is null || id.Length < 2 || id[0] != 'i')
        {
            return 0;
        }

        var digits = id.AsSpan(1);
        if (!long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return 0L;
        }

        return parsed;
    }

    /// <summary>
    /// Returns the next identity.
    /// </summary>
    /// <returns>A row identity of the form <c>i12</c>.</returns>
    public string Next()
    {
        var value = Interlocked.Increment(ref _next);
        return string.Create(CultureInfo.InvariantCulture, $"i{value}");
    }
}
