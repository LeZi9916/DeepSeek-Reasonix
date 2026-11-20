namespace Reasonix.Studio.Shell.Host;

/// <summary>
/// Which shell the page is running in, as far as anything may ask.
/// </summary>
public enum ShellKind
{
    /// <summary>
    /// A native window hosting the shell.
    /// </summary>
    Native,

    /// <summary>
    /// A plain browser tab, where the page's own keys are the reader's.
    /// </summary>
    Browser,
}

/// <summary>
/// The two facts a layout needs from the shell, and nothing else.
/// </summary>
/// <param name="Shell">Which shell answered.</param>
/// <param name="Platform">"darwin", "windows" or "linux", or empty where the page cannot know.</param>
/// <param name="TitleBar">
/// Whether the window has no native title bar, so the top row of the page is one.
/// Only the shell knows how its window was created.
/// </param>
public sealed record HostInfo(ShellKind Shell, string Platform, bool TitleBar);
