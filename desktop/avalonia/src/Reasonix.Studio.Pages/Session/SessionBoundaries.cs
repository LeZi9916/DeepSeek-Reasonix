using Reasonix.Studio.Contracts.Refusal;

namespace Reasonix.Studio.Pages.Session;

/// <summary>
/// Reads a refused switch as the boundary it hit.
/// </summary>
/// <remarks>
/// The kernel already decided which boundary this was — a turn owning the
/// session, a transcript that is gone, a history past the read limit, a path
/// that is not a transcript. This class only reads that decision back, from the
/// status and the code; it never inspects the sentence, because a sentence is
/// prose an operator wrote and a judgement copied from prose misfires on real
/// input.
/// </remarks>
public static class SessionBoundaries
{
    /// <summary>
    /// Reads one refusal as a boundary.
    /// </summary>
    /// <param name="refusal">The refusal the kernel answered with.</param>
    /// <returns>The boundary, or <see cref="SessionBoundary.Unknown"/> when the refusal names another cause.</returns>
    public static SessionBoundary Of(IRefusal refusal)
    {
        if (refusal is null)
        {
            return SessionBoundary.Unknown;
        }

        return Of(refusal.Status, refusal.Code);
    }

    /// <summary>
    /// Reads one refusal as a boundary, from the two identities it carries.
    /// </summary>
    /// <param name="status">The HTTP status the kernel answered with.</param>
    /// <param name="code">The dotted code, or null when the answer carried none.</param>
    /// <returns>The boundary the pair names.</returns>
    /// <remarks>
    /// The code wins where both are present: a kernel that answers 400 for two
    /// different causes is exactly the case a status alone cannot separate. The
    /// status is the fallback for a plain-text refusal, which the kernel writes
    /// where it has no code — the 409 a running turn answers <c>/resume</c> with
    /// is one of those.
    /// </remarks>
    public static SessionBoundary Of(int status, string? code)
    {
        if (!string.IsNullOrEmpty(code))
        {
            switch (code)
            {
                case "session.unknown":
                    return SessionBoundary.Gone;

                case "session.replay_limit":
                    return SessionBoundary.TooLarge;

                case "session.bad_path":
                case "session.disabled":
                case "request.missing_field":
                    return SessionBoundary.Invalid;

                case "session.outside_dir":
                    return SessionBoundary.Outside;

                case "busy.session_in_use":
                case "busy.session_running":
                case "busy.session_active":
                    return SessionBoundary.Held;
            }
        }

        return status switch
        {
            404 => SessionBoundary.Gone,
            409 => SessionBoundary.Held,
            413 => SessionBoundary.TooLarge,
            403 => SessionBoundary.Outside,
            400 => SessionBoundary.Invalid,
            _ => SessionBoundary.Unknown,
        };
    }

    /// <summary>
    /// Reports whether a boundary asks the person to choose another row.
    /// </summary>
    /// <param name="boundary">The boundary.</param>
    /// <returns>True for a transcript that is gone or a path that cannot be opened.</returns>
    /// <remarks>
    /// The window uses this to keep the conversation that was open on screen: a
    /// switch that cannot land must not blank what the person is reading.
    /// </remarks>
    public static bool KeepsTheOpenConversation(SessionBoundary boundary)
    {
        return boundary is SessionBoundary.Unknown
            or SessionBoundary.Gone
            or SessionBoundary.TooLarge
            or SessionBoundary.Invalid
            or SessionBoundary.Outside;
    }
}
