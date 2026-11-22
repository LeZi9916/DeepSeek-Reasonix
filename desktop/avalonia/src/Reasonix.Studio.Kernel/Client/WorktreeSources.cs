using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>Branches a new worktree may start from.</summary>
public sealed record WorktreeSources
{
    /// <summary>Gets or initializes a value indicating whether isolation is currently available.</summary>
    [JsonPropertyName("available")]
    public bool Available
    {
        get; init;
    }

    /// <summary>Gets the root the branches belong to.</summary>
    [JsonPropertyName("sourceRoot")]
    public string SourceRoot
    {
        get; init;
    } = string.Empty;

    /// <summary>Gets the offered branches.</summary>
    [JsonPropertyName("sources")]
    public IReadOnlyList<WorktreeSource> Sources
    {
        get; init;
    } = [];
}
