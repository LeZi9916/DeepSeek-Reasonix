using Reasonix.Studio.Contracts.Events;
using System.Collections.Generic;

namespace Reasonix.Studio.Kernel.Events;

/// <summary>
/// What a replay answered: the frames a reader missed, and whether the log could
/// still reach back far enough to cover them.
/// </summary>
/// <param name="Frames">The numbered frames after the requested position, in order.</param>
/// <param name="Complete">
/// Whether the replay covers every frame from that position. False means the log
/// had already shed some, and the caller has to re-read a snapshot instead.
/// </param>
/// <param name="Watermark">Where the stream stood when the replay was answered.</param>
public sealed record ReplayResult(IReadOnlyList<WireFrame> Frames, bool Complete, long Watermark);
