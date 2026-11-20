using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>
/// One window onto the output of one tool call, as the kernel's history carries it.
/// </summary>
/// <remarks>
/// Read from <c>GET /history/tool/{id}</c>. The kernel already bounds a result for
/// the model; a window bounds what a reader asks for at a time. <see cref="Bytes"/>
/// is the whole length, <see cref="Offset"/> where <see cref="Text"/> starts, and
/// <see cref="Truncated"/> says there is more after it.
/// </remarks>
public sealed record ToolOutputPage
{
    /// <summary>
    /// Gets the call this window belongs to.
    /// </summary>
    [JsonPropertyName("callId")]
    public string CallId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the text this window carries.
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Gets the whole output's length, in bytes.
    /// </summary>
    [JsonPropertyName("bytes")]
    public int Bytes
    {
        get; init;
    }

    /// <summary>
    /// Gets where <see cref="Text"/> starts, in bytes.
    /// </summary>
    [JsonPropertyName("offset")]
    public int Offset
    {
        get; init;
    }

    /// <summary>
    /// Gets a value indicating whether more output follows this window.
    /// </summary>
    [JsonPropertyName("truncated")]
    public bool Truncated
    {
        get; init;
    }
}
