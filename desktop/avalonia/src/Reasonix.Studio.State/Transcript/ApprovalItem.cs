using Reasonix.Studio.Contracts.Wire;

namespace Reasonix.Studio.State.Transcript;

/// <summary>
/// A call waiting on the person's decision.
/// </summary>
/// <remarks>
/// It holds a place of its own rather than joining the tool row it belongs to: a
/// decision is what the turn is waiting on, and a card that scrolled away with the
/// call would leave the turn looking stalled with nothing to answer.
/// </remarks>
public sealed record ApprovalItem : TranscriptItem
{
    /// <summary>
    /// Gets the approval the kernel asked for.
    /// </summary>
    public required Approval Approval
    {
        get; init;
    }

    /// <summary>
    /// Gets the verdict once the person gave one, or null while it is open.
    /// </summary>
    public string? Verdict
    {
        get; init;
    }

    /// <summary>
    /// Gets a value indicating whether the decision is still owed.
    /// </summary>
    public bool Open => Verdict is null;
}
