using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>The hub-issued address and identity of one conversation runtime.</summary>
public sealed record RuntimeDescriptor
{
    /// <summary>Gets the hub runtime identifier.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the same-origin runtime route.</summary>
    [JsonPropertyName("base")]
    public string Base { get; init; } = string.Empty;

    /// <summary>Gets the canonical working directory.</summary>
    [JsonPropertyName("root")]
    public string Root { get; init; } = string.Empty;

    /// <summary>Gets the project display name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the saved transcript, if any.</summary>
    [JsonPropertyName("sessionPath")]
    public string SessionPath { get; init; } = string.Empty;

    /// <summary>Gets the remote address-book name, empty for this machine.</summary>
    [JsonPropertyName("host")]
    public string Host { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether this runtime permits observation only.</summary>
    [JsonPropertyName("readOnly")]
    public bool ReadOnly
    {
        get; init;
    }
}
