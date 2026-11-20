namespace Reasonix.Studio.State.Transcript;

/// <summary>
/// How a foldable part of the transcript starts.
/// </summary>
/// <remarks>
/// Ported from the reference frontend's <c>state/prefs.ts</c>. The mode is a
/// setting, and the four modes that are not <see cref="Folded"/> are ways of
/// saying "open when this is true": a live one is open while the turn runs, a
/// failed one opens where something went wrong, a changed one opens where a file
/// was touched. Keeping them as data is what lets a reader set the policy once
/// rather than opening each block by hand.
/// </remarks>
public enum FoldMode
{
    /// <summary>
    /// Closed until the reader opens it.
    /// </summary>
    Folded,

    /// <summary>
    /// Open while the turn it belongs to is running.
    /// </summary>
    Live,

    /// <summary>
    /// Open when something inside it failed.
    /// </summary>
    Failed,

    /// <summary>
    /// Open when something inside it changed a file.
    /// </summary>
    Changed,

    /// <summary>
    /// Always open.
    /// </summary>
    Open,
}
