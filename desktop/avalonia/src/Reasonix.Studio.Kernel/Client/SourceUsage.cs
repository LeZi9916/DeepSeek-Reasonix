using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>Request and cost accounting for one host-declared source.</summary>
public sealed record SourceUsage
{
    /// <summary>Gets the canonical usage source.</summary>
    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    /// <summary>Gets requests with reported usage.</summary>
    [JsonPropertyName("requests")]
    public long Requests
    {
        get; init;
    }

    /// <summary>Gets billed prompt tokens.</summary>
    [JsonPropertyName("promptTokens")]
    public long PromptTokens
    {
        get; init;
    }

    /// <summary>Gets billed output tokens.</summary>
    [JsonPropertyName("completionTokens")]
    public long CompletionTokens
    {
        get; init;
    }

    /// <summary>Gets cached prompt tokens.</summary>
    [JsonPropertyName("cacheHit")]
    public long CacheHit
    {
        get; init;
    }

    /// <summary>Gets uncached and unsplit prompt tokens.</summary>
    [JsonPropertyName("cacheMiss")]
    public long CacheMiss
    {
        get; init;
    }

    /// <summary>Gets prompt tokens with no cache split.</summary>
    [JsonPropertyName("cacheUnknown")]
    public long CacheUnknown
    {
        get; init;
    }

    /// <summary>Gets the cost and coverage quoted by the host.</summary>
    [JsonPropertyName("cost")]
    public Contracts.Wire.CostQuote? Cost
    {
        get; init;
    }
}
