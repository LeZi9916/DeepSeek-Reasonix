using Reasonix.Studio.Contracts.Queue;
using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>
/// One queued entry read back whole: its metadata and the text it carries.
/// </summary>
/// <remarks>
/// Taking a line back is this read followed by a cancel. The manifest keeps only
/// a preview, so cancelling first would leave the text nowhere to be put back
/// into the composer — the line would be lost by the action meant to save it.
/// </remarks>
public sealed record QueueEntry
{
    /// <summary>
    /// Gets the entry's metadata, as the queue lists it.
    /// </summary>
    [JsonPropertyName("meta")]
    public QueueItem Meta { get; init; } = new QueueItem();

    /// <summary>
    /// Gets the text the entry carries.
    /// </summary>
    [JsonPropertyName("envelope")]
    public PromptEnvelope Envelope { get; init; } = new PromptEnvelope();
}
