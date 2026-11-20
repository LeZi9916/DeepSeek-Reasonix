using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>
/// The session a tail copy became, and how much of the original it left behind.
/// </summary>
/// <remarks>
/// A history past the safe read limit cannot open whole. The newest part can be
/// carried into a new transcript; the original file is never touched, so the
/// choice is reversible by opening the original elsewhere.
/// </remarks>
public sealed record SessionTail
{
    /// <summary>
    /// Gets the path of the new transcript holding the most recent part.
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Gets how many messages were left behind in the original.
    /// </summary>
    [JsonPropertyName("dropped")]
    public int Dropped
    {
        get; init;
    }
}
