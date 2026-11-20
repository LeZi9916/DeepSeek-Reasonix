using System.Collections.Generic;

namespace Reasonix.Studio.State.Transcript;

/// <summary>
/// One lane of the transcript: either a group of parallel delegates, or the
/// ordinary rows between such groups.
/// </summary>
/// <param name="Delegates">The delegates, in order; empty for an ordinary run.</param>
/// <param name="Rows">The ordinary rows, in order; empty for a delegate group.</param>
/// <remarks>
/// Exactly one of the two lists is non-empty, which is what lets a page draw a
/// lane without asking which kind it is.
/// </remarks>
public sealed record TranscriptLane(IReadOnlyList<ToolItem> Delegates, IReadOnlyList<TranscriptItem> Rows)
{
    /// <summary>
    /// Gets a value indicating whether this lane is a group of delegates.
    /// </summary>
    public bool IsDelegateGroup => Delegates.Count > 0;
}
