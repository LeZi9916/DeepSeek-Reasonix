using System.Text.Json.Serialization;

namespace Reasonix.Studio.Contracts.Queue;

/// <summary>
/// One reference a queued line froze, reduced to the fact that it has a path.
/// </summary>
public sealed record QueueRef
{
    /// <summary>
    /// Gets the path the entry quotes, or empty for a reference with none.
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;
}
