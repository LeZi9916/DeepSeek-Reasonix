namespace Reasonix.Studio.Pages.Rail;

/// <summary>One navigation identity across machines, workspaces and saved conversations.</summary>
/// <param name="Host">Address-book host name, empty for this machine.</param>
/// <param name="WorkspaceRoot">Canonical project working directory.</param>
/// <param name="SessionPath">Canonical transcript, empty for a new session.</param>
public sealed record SessionOpenTarget(string Host, string WorkspaceRoot, string SessionPath);
