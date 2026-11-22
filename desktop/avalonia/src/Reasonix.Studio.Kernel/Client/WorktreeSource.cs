using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>One local or remote-tracking branch offered as a worktree source.</summary>
public sealed record WorktreeSource
{
    /// <summary>Gets the full git ref.</summary>
    [JsonPropertyName("ref")]
    public string Ref
    {
        get; init;
    } = string.Empty;

    /// <summary>Gets the branch name shown to a reader.</summary>
    [JsonPropertyName("name")]
    public string Name
    {
        get; init;
    } = string.Empty;

    /// <summary>Gets or initializes a value indicating whether this is a remote-tracking branch.</summary>
    [JsonPropertyName("remote")]
    public bool Remote
    {
        get; init;
    }

    /// <summary>Gets or initializes a value indicating whether the branch is checked out now.</summary>
    [JsonPropertyName("current")]
    public bool Current
    {
        get; init;
    }
}
