using System.Text.Json.Serialization;

namespace Reasonix.Studio.Baseline;

/// <summary>
/// A rectangle as the browser reported it, in CSS pixels relative to the viewport.
/// </summary>
public sealed class BaselineBox
{
    /// <summary>
    /// Gets the distance from the viewport's left edge to this box's left edge.
    /// </summary>
    [JsonPropertyName("x")]
    public double X
    {
        get; init;
    }

    /// <summary>
    /// Gets the distance from the viewport's top edge to this box's top edge.
    /// </summary>
    [JsonPropertyName("y")]
    public double Y
    {
        get; init;
    }

    /// <summary>
    /// Gets the box's width.
    /// </summary>
    [JsonPropertyName("w")]
    public double Width
    {
        get; init;
    }

    /// <summary>
    /// Gets the box's height.
    /// </summary>
    [JsonPropertyName("h")]
    public double Height
    {
        get; init;
    }
}
