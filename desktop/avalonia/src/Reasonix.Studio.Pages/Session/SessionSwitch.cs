namespace Reasonix.Studio.Pages.Session;

/// <summary>
/// Orders concurrent opens of a conversation, so only the latest one decides
/// what is on screen.
/// </summary>
/// <remarks>
/// Ported from the reference frontend's <c>ui/sessionswitch.ts</c>. A person can
/// click one row, change their mind and click another before the first switch
/// has answered. Without a ticket the slower answer wins whenever it lands last
/// — the conversation they abandoned — and a refusal from the abandoned open
/// paints its failure over the one they chose.
///
/// The ticket is a number, not a flag: "is this still the latest?" is a
/// comparison, and a flag cannot tell the third open from the second.
/// </remarks>
public sealed class SessionSwitch
{
    /// <summary>
    /// The number handed to the latest open.
    /// </summary>
    private long _latest;

    /// <summary>
    /// The path being opened, or null when nothing is.
    /// </summary>
    private string? _path;

    /// <summary>
    /// Gets the path being opened, or empty when nothing is.
    /// </summary>
    public string Path => _path ?? string.Empty;

    /// <summary>
    /// Starts one open and returns its ticket.
    /// </summary>
    /// <param name="path">The transcript the open names, or empty for a new session.</param>
    /// <returns>The ticket, which the caller carries to every later question.</returns>
    public long Begin(string path)
    {
        _latest++;
        _path = path ?? string.Empty;
        return _latest;
    }

    /// <summary>
    /// Reports whether this path is the one being opened.
    /// </summary>
    /// <param name="candidate">The transcript a row names.</param>
    /// <returns>True when the same non-empty path is in flight.</returns>
    /// <remarks>
    /// A second click on the row already opening is the same request, not a new
    /// one: restarting it would abort work the kernel has already done.
    /// </remarks>
    public bool Opening(string candidate)
    {
        return !string.IsNullOrEmpty(candidate)
            && !string.IsNullOrEmpty(_path)
            && string.Equals(_path, candidate, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reports whether a ticket is still the latest open.
    /// </summary>
    /// <param name="ticket">The ticket the caller holds.</param>
    /// <returns>True when no later open has begun.</returns>
    public bool IsCurrent(long ticket)
    {
        return ticket == _latest;
    }

    /// <summary>
    /// Forgets the open a ticket began.
    /// </summary>
    /// <param name="ticket">The ticket the caller holds.</param>
    /// <remarks>
    /// Only the latest ticket clears the path. An older open finishing late must
    /// leave the newer one's selection and its in-flight mark alone.
    /// </remarks>
    public void End(long ticket)
    {
        if (!IsCurrent(ticket))
        {
            return;
        }

        _path = null;
    }

    /// <summary>
    /// Forgets whatever is being opened, so the next open starts clean.
    /// </summary>
    public void Cancel()
    {
        _latest++;
        _path = null;
    }
}
