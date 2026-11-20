using Reasonix.Studio.Contracts.Wire;

namespace Reasonix.Studio.State.Transcript;

/// <summary>
/// The safety assessment the kernel made of one call.
/// </summary>
public sealed record GuardianItem : TranscriptItem
{
    /// <summary>
    /// Gets the assessment itself.
    /// </summary>
    public required Guardian Assessment
    {
        get; init;
    }
}
