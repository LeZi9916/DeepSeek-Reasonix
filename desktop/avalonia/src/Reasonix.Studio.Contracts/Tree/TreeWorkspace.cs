using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Reasonix.Studio.Contracts.Tree;

/// <summary>
/// One folder in the sidebar: what it is called, whether a pane is driving it,
/// and the conversations saved under it.
/// </summary>
/// <remarks>
/// <see cref="Remembered"/> is the field that separates a project from the folder
/// a window merely happened to launch in: every window has a root, and only a
/// folder the person chose is one they expect to see listed.
/// </remarks>
public sealed record TreeWorkspace
{
    /// <summary>
    /// Gets how far the listing has got: <c>discovering</c>, <c>indexing</c>,
    /// <c>ready</c> or <c>degraded</c>.
    /// </summary>
    [JsonPropertyName("listingState")]
    public string ListingState { get; init; } = string.Empty;

    /// <summary>
    /// Gets the revision of the listing, when the kernel counts one.
    /// </summary>
    [JsonPropertyName("listingRevision")]
    public ulong? ListingRevision
    {
        get; init;
    }

    /// <summary>
    /// Gets whether the counts cover every conversation, or null when unsaid.
    /// </summary>
    [JsonPropertyName("countsComplete")]
    public bool? CountsComplete
    {
        get; init;
    }

    /// <summary>
    /// Gets how many conversations still await their metadata.
    /// </summary>
    [JsonPropertyName("pendingMetadata")]
    public int PendingMetadata
    {
        get; init;
    }

    /// <summary>
    /// Gets how many conversations' metadata could not be read.
    /// </summary>
    [JsonPropertyName("failedMetadata")]
    public int FailedMetadata
    {
        get; init;
    }

    /// <summary>
    /// Gets where this page began in a recent-ordered listing, or null for an
    /// ordinary per-project window.
    /// </summary>
    [JsonPropertyName("recentOffset")]
    public int? RecentOffset
    {
        get; init;
    }

    /// <summary>
    /// Gets the folder's path, which is the workspace's identity.
    /// </summary>
    [JsonPropertyName("root")]
    public string Root { get; init; } = string.Empty;

    /// <summary>
    /// Gets the folder's own name, which the sidebar draws.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the working directories belonging to this project.
    /// </summary>
    [JsonPropertyName("workspaceRoots")]
    public IReadOnlyList<string> WorkspaceRoots { get; init; } = new List<string>();

    /// <summary>
    /// Gets a value indicating whether the project runs in a managed worktree.
    /// </summary>
    [JsonPropertyName("isolated")]
    public bool Isolated
    {
        get; init;
    }

    /// <summary>
    /// Gets a value indicating whether the folder is no longer on disk.
    /// </summary>
    [JsonPropertyName("missing")]
    public bool Missing
    {
        get; init;
    }

    /// <summary>
    /// Gets a value indicating whether a pane is driving this folder.
    /// </summary>
    [JsonPropertyName("open")]
    public bool Open
    {
        get; init;
    }

    /// <summary>
    /// Gets a value indicating whether the person chose this folder.
    /// </summary>
    [JsonPropertyName("remembered")]
    public bool Remembered
    {
        get; init;
    }

    /// <summary>
    /// Gets how many conversations the folder lists, loaded or not.
    /// </summary>
    [JsonPropertyName("total")]
    public int Total
    {
        get; init;
    }

    /// <summary>
    /// Gets how many of them are archived.
    /// </summary>
    [JsonPropertyName("archived")]
    public int Archived
    {
        get; init;
    }

    /// <summary>
    /// Gets how many matching rows this page does not carry.
    /// </summary>
    /// <remarks>
    /// Always sent, so a client can tell a kernel that predates paging from one
    /// with nothing left to show.
    /// </remarks>
    [JsonPropertyName("remaining")]
    public int Remaining
    {
        get; init;
    }

    /// <summary>
    /// Gets the conversations the page carries, newest of them first.
    /// </summary>
    [JsonPropertyName("sessions")]
    public IReadOnlyList<TreeSession> Sessions { get; init; } = new List<TreeSession>();
}
