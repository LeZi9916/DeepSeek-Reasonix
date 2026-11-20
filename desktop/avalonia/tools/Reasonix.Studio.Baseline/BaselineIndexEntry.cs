using System.Text.Json.Serialization;

namespace Reasonix.Studio.Baseline;

/// <summary>
/// One captured domain in the baseline index.
/// </summary>
public sealed class BaselineIndexEntry
{
    /// <summary>
    /// Gets the domain name, as <c>page/scheme</c>.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name
    {
        get; init;
    } = string.Empty;

    /// <summary>
    /// Gets the file the domain is stored in, relative to the baseline directory.
    /// </summary>
    [JsonPropertyName("file")]
    public string File
    {
        get; init;
    } = string.Empty;

    /// <summary>
    /// Gets how many elements the domain holds.
    /// </summary>
    [JsonPropertyName("nodes")]
    public int Nodes
    {
        get; init;
    }

    /// <summary>
    /// Gets how many distinct styles the domain holds.
    /// </summary>
    [JsonPropertyName("styles")]
    public int Styles
    {
        get; init;
    }
}
