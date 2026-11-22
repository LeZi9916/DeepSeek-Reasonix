using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>The vendor billing window declared by the host.</summary>
public sealed record ModelTariff
{
    /// <summary>Gets a value indicating whether the current window uses peak rates.</summary>
    [JsonPropertyName("peak")]
    public bool Peak
    {
        get; init;
    }

    /// <summary>Gets the next host-declared rate change.</summary>
    [JsonPropertyName("changesAt")]
    public string? ChangesAt
    {
        get; init;
    }
}
