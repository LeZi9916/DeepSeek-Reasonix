namespace Reasonix.Studio.State.Transcript;

/// <summary>
/// How long a finished turn ran, as the kernel measured it.
/// </summary>
/// <remarks>
/// Nothing on this screen times it: a local clock would disagree with a reload.
/// Under half a second the figure rounds to nothing worth a line, so no row is
/// made at all.
/// </remarks>
public sealed record WorkedItem : TranscriptItem
{
    /// <summary>
    /// Gets the duration in milliseconds.
    /// </summary>
    public long Milliseconds
    {
        get; init;
    }

    /// <summary>
    /// Gets when the turn began, in unix milliseconds, when the kernel said.
    /// </summary>
    public long? StartedAt
    {
        get; init;
    }
}
