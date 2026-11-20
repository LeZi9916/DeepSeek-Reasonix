using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Reasonix.Studio.Baseline;

/// <summary>
/// One page domain captured from the reference frontend: a viewport, the tokens
/// it resolved, the styles its elements repeated, and every element.
/// </summary>
public sealed class BaselineDomain
{
    /// <summary>
    /// Gets the viewport the domain was captured at.
    /// </summary>
    [JsonPropertyName("viewport")]
    public BaselineViewport Viewport
    {
        get; init;
    } = new BaselineViewport();

    /// <summary>
    /// Gets the colour scheme the domain was captured under.
    /// </summary>
    [JsonPropertyName("scheme")]
    public string Scheme
    {
        get; init;
    } = string.Empty;

    /// <summary>
    /// Gets the CSS custom properties resolved on the root element, sorted by name.
    /// </summary>
    [JsonPropertyName("tokens")]
    public Dictionary<string, string> Tokens { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Gets the deduplicated style table the nodes index into.
    /// </summary>
    [JsonPropertyName("styles")]
    public List<Dictionary<string, string>> Styles { get; init; } = new List<Dictionary<string, string>>();

    /// <summary>
    /// Gets every element the collector kept.
    /// </summary>
    [JsonPropertyName("nodes")]
    public List<BaselineNode> Nodes { get; init; } = new List<BaselineNode>();
}
