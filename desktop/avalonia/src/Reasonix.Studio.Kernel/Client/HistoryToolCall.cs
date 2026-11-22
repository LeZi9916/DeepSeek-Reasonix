using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>A persisted model tool call.</summary>
public sealed record HistoryToolCall
{
    /// <summary>Gets the host call identifier.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the provider-visible name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the submitted arguments.</summary>
    [JsonPropertyName("arguments")]
    public string Arguments { get; init; } = string.Empty;

    /// <summary>Gets the effective capability name.</summary>
    [JsonPropertyName("resolvedName")]
    public string? ResolvedName
    {
        get; init;
    }

    /// <summary>Gets the canonical capability identity.</summary>
    [JsonPropertyName("capabilityId")]
    public string? CapabilityId
    {
        get; init;
    }

    /// <summary>Gets the host argument repair record.</summary>
    [JsonPropertyName("argumentNormalization")]
    public Contracts.Wire.ArgumentNormalization? ArgumentNormalization
    {
        get; init;
    }
}
