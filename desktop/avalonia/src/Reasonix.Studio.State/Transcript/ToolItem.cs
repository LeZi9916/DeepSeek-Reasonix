using Reasonix.Studio.Contracts.Wire;
using System.Collections.Generic;

namespace Reasonix.Studio.State.Transcript;

/// <summary>
/// One tool call, with the calls a sub-agent made inside it.
/// </summary>
public sealed record ToolItem : TranscriptItem
{
    /// <summary>
    /// Gets the call itself.
    /// </summary>
    public required Tool Call
    {
        get; init;
    }

    /// <summary>
    /// Gets a value indicating whether the call is still running.
    /// </summary>
    public bool Running
    {
        get; init;
    }

    /// <summary>
    /// Gets the calls this one spawned, in the order they first appeared.
    /// </summary>
    public IReadOnlyList<Tool> Children { get; init; } = System.Array.Empty<Tool>();

    /// <summary>
    /// Gets how many characters of a running call's output the page let go of
    /// from the front.
    /// </summary>
    public int? Omitted
    {
        get; init;
    }

    /// <summary>
    /// Gets how many characters of a finished call's middle the page let go of.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Omitted"/>: this one was dropped after the call
    /// settled, from between its two kept ends, and the card offers to read the
    /// whole thing back from the kernel. Omitted is what a running call's progress
    /// stream overran.
    /// </remarks>
    public int Hidden
    {
        get; init;
    }
}
