namespace Reasonix.Studio.State.Transcript;

/// <summary>
/// A fact the agent saved during the turn.
/// </summary>
public sealed record RememberItem : TranscriptItem
{
    /// <summary>
    /// Gets what was saved.
    /// </summary>
    public required RememberedFact Fact
    {
        get; init;
    }
}
