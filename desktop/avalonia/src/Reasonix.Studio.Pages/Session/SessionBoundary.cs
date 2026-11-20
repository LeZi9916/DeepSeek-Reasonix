namespace Reasonix.Studio.Pages.Session;

/// <summary>
/// Why a conversation cannot be opened, as the thing the person can do next.
/// </summary>
/// <remarks>
/// The kernel already names the cause with a status and a dotted code; this is
/// the window's reading of it, and it exists so the page never decides by
/// matching a sentence. Each member is one different next step: wait, forget the
/// row, carry the tail over, pick another row, or nothing at all.
/// </remarks>
public enum SessionBoundary
{
    /// <summary>
    /// None of the boundaries this file names; the refusal says the rest.
    /// </summary>
    Unknown,

    /// <summary>
    /// A turn holds the session: the answer is to wait or stop it.
    /// </summary>
    Held,

    /// <summary>
    /// The transcript is gone: the row was drawn before the file did.
    /// </summary>
    Gone,

    /// <summary>
    /// The history is past the safe read limit: it cannot open whole.
    /// </summary>
    TooLarge,

    /// <summary>
    /// The path is not a transcript name, or the request named nothing.
    /// </summary>
    Invalid,

    /// <summary>
    /// The path is outside the conversations directory the kernel serves.
    /// </summary>
    Outside,
}
