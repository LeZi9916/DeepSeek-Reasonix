using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>One provider's last connection and wallet reading.</summary>
public sealed record ProviderHealth
{
    /// <summary>Gets the connection state.</summary>
    [JsonPropertyName("connection")]
    public string Connection
    {
        get; init;
    } = "unknown";

    /// <summary>Gets the wallet state.</summary>
    [JsonPropertyName("wallet")]
    public string Wallet
    {
        get; init;
    } = "unknown";

    /// <summary>Gets the Unix timestamp of the reading.</summary>
    [JsonPropertyName("checkedAt")]
    public long CheckedAt
    {
        get; init;
    }
}
