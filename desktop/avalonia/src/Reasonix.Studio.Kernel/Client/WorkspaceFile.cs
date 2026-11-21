namespace Reasonix.Studio.Kernel.Client;

/// <summary>An editable workspace snapshot, including the version required to save it.</summary>
/// <param name="Path">Workspace-relative path.</param>
/// <param name="Content">Text as read or edited.</param>
/// <param name="Revision">Revision supplied by the read.</param>
/// <param name="ObservedVersion">Workspace observation accompanying the read.</param>
/// <param name="Root">Granted root when outside the current workspace.</param>
/// <param name="ReadOnly">Whether the host permits only reading.</param>
public sealed record WorkspaceFile(
    string Path,
    string Content,
    string Revision,
    string? ObservedVersion = null,
    string? Root = null,
    bool ReadOnly = false);
