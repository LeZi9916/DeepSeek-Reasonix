namespace Reasonix.Studio.Pages.Rail;

/// <summary>An editor supplied by the host's actual installed-program catalog.</summary>
/// <param name="Id">Canonical program identifier.</param><param name="Name">Host-supplied program name.</param>
/// <param name="Available">Whether the host allows this program.</param><param name="Why">Canonical unavailability reason.</param>
public sealed record WorkspaceOpenTarget(string Id, string Name, bool Available, string Why);
