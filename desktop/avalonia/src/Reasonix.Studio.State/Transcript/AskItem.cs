using Reasonix.Studio.Contracts.Wire;

namespace Reasonix.Studio.State.Transcript;

/// <summary>
/// A question the model asked the person, waiting for an answer.
/// </summary>
/// <remarks>
/// It keeps its own place rather than joining a tool row: the turn is waiting on
/// this answer, and a card that scrolled away with its call would leave the turn
/// looking stalled with nothing to press.
/// </remarks>
public sealed record AskItem : TranscriptItem
{
    /// <summary>
    /// Gets the question the kernel sent.
    /// </summary>
    public required Ask Ask
    {
        get; init;
    }

    /// <summary>
    /// Gets the answers already given, one set per question, or null while it is open.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>>? Answered
    {
        get; init;
    }
}
