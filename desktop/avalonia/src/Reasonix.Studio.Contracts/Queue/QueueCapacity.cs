using System.Text.Json.Serialization;

namespace Reasonix.Studio.Contracts.Queue;

/// <summary>
/// What the queue holds against the limits it refuses on.
/// </summary>
/// <remarks>
/// Both limits bite — a queue refuses on count and on total bytes — and a panel
/// that showed one of them would be wrong about the other.
/// </remarks>
public sealed record QueueCapacity
{
    /// <summary>
    /// Gets how many entries are waiting.
    /// </summary>
    [JsonPropertyName("items")]
    public int Items
    {
        get; init;
    }

    /// <summary>
    /// Gets the most entries the queue will hold.
    /// </summary>
    [JsonPropertyName("maxItems")]
    public int MaxItems
    {
        get; init;
    }

    /// <summary>
    /// Gets how many bytes the entries hold.
    /// </summary>
    [JsonPropertyName("bytes")]
    public long Bytes
    {
        get; init;
    }

    /// <summary>
    /// Gets the most bytes the queue will hold.
    /// </summary>
    [JsonPropertyName("maxBytes")]
    public long MaxBytes
    {
        get; init;
    }
}
