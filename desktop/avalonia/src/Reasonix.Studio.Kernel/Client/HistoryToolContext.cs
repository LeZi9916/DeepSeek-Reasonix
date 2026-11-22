using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>A dispatch retained at a history page boundary.</summary>
public sealed record HistoryToolContext
{
    /// <summary>Gets the dispatch message position.</summary>
    [JsonPropertyName("messageIndex")]
    public int MessageIndex
    {
        get; init;
    }

    /// <summary>Gets the call position within its message.</summary>
    [JsonPropertyName("ordinal")]
    public int Ordinal
    {
        get; init;
    }

    /// <summary>Gets the canonical dispatch.</summary>
    [JsonPropertyName("call")]
    public HistoryToolCall Call { get; init; } = new();
}
