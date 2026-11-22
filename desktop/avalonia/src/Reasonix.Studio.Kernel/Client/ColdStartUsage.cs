using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>The opening executor request, retained for warm-cache accounting.</summary>
public sealed record ColdStartUsage
{
    /// <summary>Gets billed prompt tokens.</summary>
    [JsonPropertyName("promptTokens")]
    public long PromptTokens
    {
        get; init;
    }

    /// <summary>Gets cached prompt tokens.</summary>
    [JsonPropertyName("cacheHit")]
    public long CacheHit
    {
        get; init;
    }

    /// <summary>Gets uncached and unsplit tokens.</summary>
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
}
