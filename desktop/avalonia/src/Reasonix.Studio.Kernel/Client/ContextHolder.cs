using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>A large message's measured contribution without revealing its contents.</summary>
public sealed record ContextHolder
{
    /// <summary>Gets the canonical prompt class.</summary>
    [JsonPropertyName("class")]
    public string Class { get; init; } = string.Empty;

    /// <summary>Gets the producing tool, when present.</summary>
    [JsonPropertyName("tool")]
    public string? Tool
    {
        get; init;
    }

    /// <summary>Gets the token contribution.</summary>
    [JsonPropertyName("tokens")]
    public long Tokens
    {
        get; init;
    }

    /// <summary>Gets a value indicating whether it belongs to the running turn.</summary>
    [JsonPropertyName("active")]
    public bool Active
    {
        get; init;
    }
}
