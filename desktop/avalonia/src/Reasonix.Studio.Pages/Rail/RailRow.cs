namespace Reasonix.Studio.Pages.Rail;

/// <summary>
/// One row the sidebar draws, flattened from the kernel's tree.
/// </summary>
/// <param name="Kind">Whether the row is a project or a conversation.</param>
/// <param name="Path">The transcript's path, or the project's root.</param>
/// <param name="Title">What the row says, in the reader's language.</param>
/// <param name="Subtitle">A second line: the project's session count, or a conversation's turns.</param>
/// <param name="Indent">How deep the row is drawn, in levels.</param>
/// <param name="Unread">Whether a turn finished since the person last looked.</param>
/// <param name="Pinned">Whether the conversation is pinned.</param>
/// <param name="Archived">Whether the conversation is archived.</param>
/// <param name="Running">Whether a pane already drives this transcript.</param>
/// <param name="Missing">Whether the project's folder is no longer on disk.</param>
/// <param name="Collapsed">How many copies and versions stand behind this row.</param>
public sealed record RailRow(
    RailRowKind Kind,
    string Path,
    string Title,
    string Subtitle,
    int Indent,
    bool Unread,
    bool Pinned,
    bool Archived,
    bool Running,
    bool Missing,
    int Collapsed);
