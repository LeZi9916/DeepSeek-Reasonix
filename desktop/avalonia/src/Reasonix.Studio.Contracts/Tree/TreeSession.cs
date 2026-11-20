using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Reasonix.Studio.Contracts.Tree;

/// <summary>
/// One conversation in the sidebar, as the kernel's tree lists it.
/// </summary>
/// <remarks>
/// A conversation is not always one row. A save that kept conflicting writes
/// leaves <see cref="Copies"/> — one file per turn, all under one title — and
/// every edit, regenerate or rewind leaves the conversation as it stood before,
/// newest first, in <see cref="Versions"/>. Both are the same shape, so both open
/// like any other row; unfolding them here rather than in the kernel is what
/// keeps a sidebar of rows the person never made off the screen.
/// </remarks>
public sealed record TreeSession
{
    /// <summary>
    /// Gets how far the kernel has read this conversation's metadata:
    /// <c>pending</c>, <c>ready</c> or <c>error</c>.
    /// </summary>
    [JsonPropertyName("metadataState")]
    public string MetadataState { get; init; } = string.Empty;

    /// <summary>
    /// Gets whether the turn count is known, or null when the kernel did not say.
    /// </summary>
    [JsonPropertyName("countsKnown")]
    public bool? CountsKnown
    {
        get; init;
    }

    /// <summary>
    /// Gets the transcript's path, which is what opening the row names.
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Gets the workspace the conversation ran in.
    /// </summary>
    [JsonPropertyName("workspaceRoot")]
    public string WorkspaceRoot { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the conversation ran in a managed worktree.
    /// </summary>
    [JsonPropertyName("isolated")]
    public bool Isolated
    {
        get; init;
    }

    /// <summary>
    /// Gets the name the transcript file carries.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the title the conversation gave itself, when it has one.
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Gets how many turns the conversation holds, or 0 when it is not counted.
    /// </summary>
    [JsonPropertyName("turns")]
    public int Turns
    {
        get; init;
    }

    /// <summary>
    /// Gets the pane already driving this transcript, or empty when none is.
    /// </summary>
    /// <remarks>
    /// A sidebar focuses that pane rather than opening a second writer for one
    /// file, so this is the fact that decides between the two.
    /// </remarks>
    [JsonPropertyName("runtimeId")]
    public string RuntimeId { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the conversation is archived.
    /// </summary>
    [JsonPropertyName("archived")]
    public bool Archived
    {
        get; init;
    }

    /// <summary>
    /// Gets a value indicating whether the conversation is pinned.
    /// </summary>
    [JsonPropertyName("pinned")]
    public bool Pinned
    {
        get; init;
    }

    /// <summary>
    /// Gets when the conversation was last written, in unix milliseconds, or null
    /// when the kernel does not know.
    /// </summary>
    [JsonPropertyName("lastActiveAt")]
    public long? LastActiveAt
    {
        get; init;
    }

    /// <summary>
    /// Gets when the conversation was archived automatically, in unix
    /// milliseconds, or null for a manual archive or none.
    /// </summary>
    [JsonPropertyName("autoArchivedAt")]
    public long? AutoArchivedAt
    {
        get; init;
    }

    /// <summary>
    /// Gets a value indicating whether a turn finished since the person last looked.
    /// </summary>
    [JsonPropertyName("unread")]
    public bool Unread
    {
        get; init;
    }

    /// <summary>
    /// Gets when the person last looked, in unix milliseconds.
    /// </summary>
    /// <remarks>
    /// Sent only while the row is unread: it is where the new answers begin. Null
    /// on a row that was seen, and on a kernel that predates the field.
    /// </remarks>
    [JsonPropertyName("viewedAt")]
    public long? ViewedAt
    {
        get; init;
    }

    /// <summary>
    /// Gets a value indicating whether this transcript is kept in the 1.x store.
    /// </summary>
    /// <remarks>
    /// Such a conversation is read and never written by this build, so the row
    /// says which store it came from rather than looking native.
    /// </remarks>
    [JsonPropertyName("legacy")]
    public bool Legacy
    {
        get; init;
    }

    /// <summary>
    /// Gets the conflict-recovery copies of this conversation.
    /// </summary>
    [JsonPropertyName("copies")]
    public IReadOnlyList<TreeSession> Copies { get; init; } = new List<TreeSession>();

    /// <summary>
    /// Gets this conversation as it stood before each cut, newest first.
    /// </summary>
    [JsonPropertyName("versions")]
    public IReadOnlyList<TreeSession> Versions { get; init; } = new List<TreeSession>();
}
