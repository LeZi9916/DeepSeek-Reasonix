namespace Reasonix.Studio.State;

/// <summary>
/// How a turn ended.
/// </summary>
/// <remarks>
/// Four judgements rather than a bool: a turn that stopped because obligations
/// are missing delivered nothing, and folding it in with a clean finish is what
/// let it claim the tick. Cancellation carries an error of its own, so the flag is
/// what tells a stop apart from a dropped connection.
/// </remarks>
public abstract record TurnTerminal
{
    /// <summary>
    /// Gets the terminal for a turn that ran to its end.
    /// </summary>
    public static TurnTerminal Completed { get; } = new CompletedTerminal();

    /// <summary>
    /// Gets the terminal for a turn the transcript could not read one for.
    /// </summary>
    public static TurnTerminal Unread { get; } = new UnreadTerminal();
}
