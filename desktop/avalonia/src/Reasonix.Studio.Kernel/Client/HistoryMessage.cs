using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>
/// One stored message as the kernel projects it for a frontend.
/// </summary>
public sealed record HistoryMessage
{
    /// <summary>Gets dispatches made by this model message.</summary>
    [JsonPropertyName("toolCalls")]
    public HistoryToolCall[] ToolCalls { get; init; } = [];

    /// <summary>Gets the call this result answers.</summary>
    [JsonPropertyName("toolCallId")]
    public string? ToolCallId
    {
        get; init;
    }

    /// <summary>Gets the result's canonical tool name.</summary>
    [JsonPropertyName("toolName")]
    public string? ToolName
    {
        get; init;
    }

    /// <summary>Gets a value indicating whether the host classified the result as failed.</summary>
    [JsonPropertyName("toolFailed")]
    public bool ToolFailed
    {
        get; init;
    }

    /// <summary>Gets the host's typed refusal cause.</summary>
    [JsonPropertyName("toolRefusalCode")]
    public string? ToolRefusalCode
    {
        get; init;
    }

    /// <summary>Gets a value indicating whether the result is a unified diff.</summary>
    [JsonPropertyName("outputDiff")]
    public bool OutputDiff
    {
        get; init;
    }

    /// <summary>Gets a value indicating whether the user-role line was authored by the host.</summary>
    [JsonPropertyName("hostAuthored")]
    public bool HostAuthored
    {
        get; init;
    }

    /// <summary>Gets the measured reasoning duration.</summary>
    [JsonPropertyName("thoughtMs")]
    public long? ThoughtMs
    {
        get; init;
    }

    /// <summary>
    /// Gets who wrote the message: <c>user</c>, <c>assistant</c> or <c>tool</c>.
    /// </summary>
    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    /// <summary>
    /// Gets the message's text.
    /// </summary>
    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Gets the reasoning that preceded an assistant message.
    /// </summary>
    [JsonPropertyName("reasoning")]
    public string Reasoning { get; init; } = string.Empty;

    /// <summary>
    /// Gets the session index this message occupies.
    /// </summary>
    [JsonPropertyName("msgIndex")]
    public int MsgIndex
    {
        get; init;
    }

    /// <summary>
    /// Gets when a user message landed, in unix milliseconds.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public long CreatedAt
    {
        get; init;
    }

    /// <summary>
    /// Gets which model wrote an assistant message.
    /// </summary>
    [JsonPropertyName("modelRef")]
    public string ModelRef { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether guidance was said into a running turn.
    /// </summary>
    /// <remarks>
    /// It never was a turn of its own, so it has no checkpoint and nothing rewinds
    /// to it.
    /// </remarks>
    [JsonPropertyName("steer")]
    public bool Steer
    {
        get; init;
    }
}
