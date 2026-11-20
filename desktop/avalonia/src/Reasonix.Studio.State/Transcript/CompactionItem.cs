using Reasonix.Studio.Contracts.Wire;

namespace Reasonix.Studio.State.Transcript;

/// <summary>
/// One fold of the conversation so far.
/// </summary>
/// <remarks>
/// The card stays from the start of the fold to its end, so a reader who looked
/// mid-way finds the same place afterwards and the summary grows in that place
/// rather than arriving as a second card.
/// </remarks>
public sealed record CompactionItem : TranscriptItem
{
    /// <summary>
    /// Gets what the kernel reported about the fold.
    /// </summary>
    public Compaction Compaction
    {
        get; init;
    } = new Compaction();

    /// <summary>
    /// Gets a value indicating whether the fold has finished.
    /// </summary>
    public bool Done
    {
        get; init;
    }
}
