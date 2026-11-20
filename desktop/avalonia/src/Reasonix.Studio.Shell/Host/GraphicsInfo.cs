namespace Reasonix.Studio.Shell.Host;

/// <summary>
/// How this launch draws.
/// </summary>
/// <param name="LaunchedOff">What the shell applied at start.</param>
/// <param name="SavedOff">The saved choice alone, or null from a shell that predates it.</param>
/// <param name="Compositing">The shell's own status string, empty when it has none yet.</param>
public sealed record GraphicsInfo(bool LaunchedOff, bool? SavedOff, string Compositing);
