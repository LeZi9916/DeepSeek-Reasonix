using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>Text the host saved so a long paste can be read on demand.</summary>
public sealed record KeptText
{
    /// <summary>Gets the saved file's path.</summary>
    [JsonPropertyName("path")]
    public string Path
    {
        get; init;
    } = string.Empty;

    /// <summary>Gets the pointer text that stands in for the paste.</summary>
    [JsonPropertyName("pointer")]
    public string Pointer
    {
        get; init;
    } = string.Empty;
}
