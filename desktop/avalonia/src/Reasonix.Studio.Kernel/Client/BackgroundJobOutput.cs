using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>The non-consuming tail of a background job's output.</summary>
public sealed record BackgroundJobOutput
{
    /// <summary>Gets the output text returned by the kernel.</summary>
    [JsonPropertyName("text")]
    public string Text
    {
        get; init;
    } = string.Empty;

    /// <summary>Gets the lifecycle state at the time of this read.</summary>
    [JsonPropertyName("status")]
    public string Status
    {
        get; init;
    } = string.Empty;
}
