using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Reasonix.Studio.Contracts.Queue;

/// <summary>
/// One line waiting in the session's queue, as the manifest keeps it.
/// </summary>
/// <remarks>
/// Metadata only: <see cref="Preview"/> is all the manifest carries, and the
/// body is a separate read. That is why taking a line back is a read followed by
/// a cancel — cancelling first would leave the text nowhere.
/// </remarks>
public sealed record QueueItem
{
    /// <summary>
    /// Gets the identity the kernel gave this entry.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets what the line will become: <c>followup</c> or <c>steer</c>.
    /// </summary>
    [JsonPropertyName("intent")]
    public string Intent { get; init; } = string.Empty;

    /// <summary>
    /// Gets who wrote it. Absent means the person; <c>host</c> is the runtime's
    /// own continuation, such as a finished background job.
    /// </summary>
    [JsonPropertyName("origin")]
    public string Origin { get; init; } = string.Empty;

    /// <summary>
    /// Gets where the entry stands: <c>queued</c>, <c>steer_accepted</c>,
    /// <c>steer_consumed</c>, <c>running</c>, <c>blocked</c> or <c>uncertain</c>.
    /// </summary>
    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;

    /// <summary>
    /// Gets the first line of the body, which is all the manifest keeps.
    /// </summary>
    [JsonPropertyName("preview")]
    public string Preview { get; init; } = string.Empty;

    /// <summary>
    /// Gets the kernel's stable identity for why a blocked entry stopped.
    /// </summary>
    [JsonPropertyName("blockCode")]
    public string BlockCode { get; init; } = string.Empty;

    /// <summary>
    /// Gets the kernel's own sentence for the stop, for a code with no wording here.
    /// </summary>
    [JsonPropertyName("blockReason")]
    public string BlockReason { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether a picture was frozen with the line.
    /// </summary>
    /// <remarks>
    /// Such a line cannot be slipped into a running turn as guidance; it is sent
    /// as a turn of its own when the current one ends.
    /// </remarks>
    [JsonPropertyName("images")]
    public bool Images
    {
        get; init;
    }

    /// <summary>
    /// Gets the files the entry froze when it was queued.
    /// </summary>
    [JsonPropertyName("refs")]
    public IReadOnlyList<QueueRef> Refs { get; init; } = new List<QueueRef>();
}
