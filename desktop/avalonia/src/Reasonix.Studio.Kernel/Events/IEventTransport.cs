using Reasonix.Studio.Contracts.Events;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Events;

/// <summary>
/// The two reads a subscription needs from whichever transport carries them.
/// </summary>
/// <remarks>
/// This is an interface rather than a concrete client so the subscription's
/// ordering rules can be tested without a socket: what a gap means, and what
/// order frames reach a reducer in, is the part worth holding, and it does not
/// depend on how the bytes arrive.
/// </remarks>
public interface IEventTransport
{
    /// <summary>
    /// Opens the live stream and keeps it open, reconnecting after a drop.
    /// </summary>
    /// <param name="cursor">
    /// Read at every connect, including each reconnect, and returns the last frame
    /// number already folded or null to attach without a resume point. It is read
    /// each time rather than captured once because a reconnect that asked for the
    /// position this call started at would replay frames the reducer already has
    /// and miss the ones it does not.
    /// </param>
    /// <param name="cancellationToken">Cancels the stream and stops reconnecting.</param>
    /// <returns>The frames, in the order the kernel published them.</returns>
    IAsyncEnumerable<WireFrame> StreamAsync(Func<long?> cursor, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the frames after a position out of the kernel's replay log.
    /// </summary>
    /// <param name="after">The last frame number already folded.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>What the log could still answer, and whether it covered the range.</returns>
    Task<ReplayResult> ReplayAsync(long after, CancellationToken cancellationToken);
}
