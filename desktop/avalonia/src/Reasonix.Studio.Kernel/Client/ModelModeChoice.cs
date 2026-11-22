using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>An optional way the session's model can run beside its effort level.</summary>
public sealed record ModelModeChoice
{
    /// <summary>Gets the kernel identity sent back when this mode is toggled.</summary>
    [JsonPropertyName("id")]
    public string Id
    {
        get; init;
    } = string.Empty;

    /// <summary>Gets the message key naming this mode.</summary>
    [JsonPropertyName("labelKey")]
    public string LabelKey
    {
        get; init;
    } = string.Empty;

    /// <summary>Gets the message key explaining this mode.</summary>
    [JsonPropertyName("hintKey")]
    public string HintKey
    {
        get; init;
    } = string.Empty;

    /// <summary>Gets a value indicating whether the mode costs more than the base run.</summary>
    [JsonPropertyName("costlier")]
    public bool Costlier
    {
        get; init;
    }

    /// <summary>Gets a value indicating whether this mode is on.</summary>
    [JsonPropertyName("active")]
    public bool Active
    {
        get; init;
    }
}
