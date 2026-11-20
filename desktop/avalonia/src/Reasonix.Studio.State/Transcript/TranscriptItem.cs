namespace Reasonix.Studio.State.Transcript;

/// <summary>
/// One row of the transcript.
/// </summary>
/// <remarks>
/// The browser client models this as a discriminated union of object literals; C#
/// expresses the same thing as a closed hierarchy, so a switch over the cases is
/// exhaustive and a new row kind cannot be added without every reader seeing it.
/// </remarks>
public abstract record TranscriptItem
{
    /// <summary>
    /// Gets the row's identity, minted from one sequence so two rows cannot collide.
    /// </summary>
    public required string Id
    {
        get; init;
    }
}
