using Reasonix.Studio.Contracts.Wire;
using System.Collections.Generic;

namespace Reasonix.Studio.State.Transcript;

/// <summary>
/// A run of lookups folded into one row.
/// </summary>
/// <remarks>
/// Four file reads in a row are one thing the turn did, not four; a lone search
/// keeps its own row because an excerpt list says more than a count would.
/// </remarks>
public sealed record ReadsItem : TranscriptItem
{
    /// <summary>
    /// Gets the calls this row stands for, in the order they ran.
    /// </summary>
    public IReadOnlyList<Tool> Tools { get; init; } = System.Array.Empty<Tool>();
}
