using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>A stable window of the kernel's projected transcript.</summary>
public sealed record HistoryPage
{
    /// <summary>Gets the projected messages in this window.</summary>
    [JsonPropertyName("messages")]
    public HistoryMessage[] Messages { get; init; } = [];

    /// <summary>Gets dispatches that precede this window.</summary>
    [JsonPropertyName("toolContext")]
    public HistoryToolContext[] ToolContext { get; init; } = [];

    /// <summary>Gets the immutable pagination identity.</summary>
    [JsonPropertyName("snapshot")]
    public string Snapshot { get; init; } = string.Empty;

    /// <summary>Gets the first included message index.</summary>
    [JsonPropertyName("start")]
    public int Start
    {
        get; init;
    }

    /// <summary>Gets the exclusive end of the window.</summary>
    [JsonPropertyName("end")]
    public int End
    {
        get; init;
    }

    /// <summary>Gets the snapshot's message count.</summary>
    [JsonPropertyName("total")]
    public int Total
    {
        get; init;
    }

    /// <summary>Gets a value indicating whether earlier messages remain.</summary>
    [JsonPropertyName("hasMoreBefore")]
    public bool HasMoreBefore
    {
        get; init;
    }

    /// <summary>Gets the opaque cursor for the preceding window.</summary>
    [JsonPropertyName("nextCursor")]
    public string? NextCursor
    {
        get; init;
    }

    /// <summary>Gets the recorded start of the running turn.</summary>
    [JsonPropertyName("turnStartedAt")]
    public long? TurnStartedAt
    {
        get; init;
    }
}
