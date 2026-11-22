using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>A standing instruction document measured by the host.</summary>
public sealed record ContextMemoryFile
{
    /// <summary>Gets the host-resolved document path.</summary>
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    /// <summary>Gets its declared scope.</summary>
    [JsonPropertyName("scope")]
    public string Scope { get; init; } = string.Empty;

    /// <summary>Gets its measured token contribution.</summary>
    [JsonPropertyName("tokens")]
    public long Tokens
    {
        get; init;
    }
}
