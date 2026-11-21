namespace Reasonix.Studio.Kernel.Client;

/// <summary>A directory listing returned by the workspace endpoint.</summary>
/// <param name="Files">Workspace-relative file paths.</param>
/// <param name="Directories">Workspace-relative directory paths.</param>
public sealed record WorkspaceListing(
    string[] Files,
    string[] Directories);
