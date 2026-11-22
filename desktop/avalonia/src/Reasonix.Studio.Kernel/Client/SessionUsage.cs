using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>Session accounting from the kernel's request ledger.</summary>
public sealed record SessionUsage
{
    /// <summary>Gets requests that reported usage.</summary>
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

    /// <summary>Gets billed completion tokens.</summary>
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

    /// <summary>Gets prompt tokens not split by the endpoint.</summary>
    [JsonPropertyName("cacheUnknown")]
    public long CacheUnknown
    {
        get; init;
    }

    /// <summary>Gets completed-turn active work time.</summary>
    [JsonPropertyName("workMs")]
    public long WorkMs
    {
        get; init;
    }

    /// <summary>Gets the sources contributing these totals.</summary>
    [JsonPropertyName("sources")]
    public SourceUsage[] Sources { get; init; } = [];

    /// <summary>Gets the first executor request already included in these totals.</summary>
    [JsonPropertyName("coldStart")]
    public ColdStartUsage? ColdStart
    {
        get; init;
    }
}
