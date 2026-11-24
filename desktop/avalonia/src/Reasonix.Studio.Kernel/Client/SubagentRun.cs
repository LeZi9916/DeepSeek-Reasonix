using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>One run in the current session's subagent record.</summary>
public sealed record SubagentRun
{
    /// <summary>Gets the stable run identifier.</summary>
    [JsonPropertyName("ref")]
    public string Ref
    {
        get; init;
    } = string.Empty;

    /// <summary>Gets the kernel's name for the subagent.</summary>
    [JsonPropertyName("name")]
    public string Name
    {
        get; init;
    } = string.Empty;

    /// <summary>Gets the model that ran this subagent.</summary>
    [JsonPropertyName("model")]
    public string Model
    {
        get; init;
    } = string.Empty;

    /// <summary>Gets the lifecycle state of the recorded run.</summary>
    [JsonPropertyName("state")]
    public string State
    {
        get; init;
    } = string.Empty;

    /// <summary>Gets the call that launched this subagent.</summary>
    [JsonPropertyName("parentCall")]
    public string? ParentCall
    {
        get; init;
    }
}
