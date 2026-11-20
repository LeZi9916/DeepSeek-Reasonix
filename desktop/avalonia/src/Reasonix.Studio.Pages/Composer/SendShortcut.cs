namespace Reasonix.Studio.Pages.Composer;

/// <summary>
/// Which key sends the composer's line.
/// </summary>
/// <remarks>
/// A machine-local preference, never a project setting: it answers what this
/// person's hands do, and a repository that carried it would impose one typist's
/// habit on everyone who opened it.
/// </remarks>
public enum SendShortcut
{
    /// <summary>
    /// Enter sends; Shift+Enter starts a new line.
    /// </summary>
    Enter,

    /// <summary>
    /// The modifier chord sends; a bare Enter starts a new line, so an input
    /// method's own Enter is never mistaken for a send.
    /// </summary>
    ModifierEnter,
}
