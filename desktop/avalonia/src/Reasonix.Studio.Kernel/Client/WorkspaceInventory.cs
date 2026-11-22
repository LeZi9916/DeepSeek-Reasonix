using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>The workspace inventory and whether this host can isolate a worktree.</summary>
public sealed record WorkspaceInventory
{
    /// <summary>Gets the current workspace base.</summary>
    [JsonPropertyName("current")]
    public string Current
    {
        get; init;
    } = string.Empty;

    /// <summary>Gets or initializes a value indicating whether this host may change workspaces.</summary>
    [JsonPropertyName("canSwitch")]
    public bool CanSwitch
    {
        get; init;
    }

    /// <summary>Gets or initializes a value indicating whether this host may create an isolated worktree.</summary>
    [JsonPropertyName("canIsolate")]
    public bool CanIsolate
    {
        get; init;
    }

    /// <summary>Gets or initializes a value indicating whether the current root is already an isolated worktree.</summary>
    [JsonPropertyName("isolated")]
    public bool Isolated
    {
        get; init;
    }
}
