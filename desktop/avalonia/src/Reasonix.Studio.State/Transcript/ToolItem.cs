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
}
