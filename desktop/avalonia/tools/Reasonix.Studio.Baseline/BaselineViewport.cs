using System.Text.Json.Serialization;

namespace Reasonix.Studio.Baseline;

/// <summary>
/// The pixel size a domain was captured at.
/// </summary>
public sealed class BaselineViewport
{
    /// <summary>
    /// Gets the viewport width in CSS pixels.
    /// </summary>
    [JsonPropertyName("w")]
    public double Width
    {
        get; init;
    }

    /// <summary>
    /// Gets the viewport height in CSS pixels.
    /// </summary>
    [JsonPropertyName("h")]
    public double Height
    {
        get; init;
    }
}
