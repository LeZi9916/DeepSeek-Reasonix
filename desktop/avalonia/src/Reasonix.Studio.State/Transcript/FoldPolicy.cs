namespace Reasonix.Studio.State.Transcript;

/// <summary>
/// What starts a foldable part open, given the part's own state.
/// </summary>
/// <remarks>
/// Ported from the reference frontend's <c>state/foldpref.ts</c>. It is a pure
/// function of the mode and the part's state, so the rule can be held by a case
/// without a window; the reader's own open or closed choice is kept by the
/// control and consulted before this.
/// </remarks>
public static class FoldPolicy
{
    /// <summary>
    /// Decides whether a part starts open.
    /// </summary>
    /// <param name="mode">How this kind of part is configured to start.</param>
    /// <param name="live">Whether the turn it belongs to is still running.</param>
    /// <param name="failed">Whether something inside it failed.</param>
    /// <param name="changed">Whether something inside it changed a file.</param>
    /// <returns>True when the part opens on its own.</returns>
    public static bool StartsOpen(FoldMode mode, bool live = false, bool failed = false, bool changed = false)
    {
        return mode == FoldMode.Open
            || (mode == FoldMode.Live && live)
            || (mode == FoldMode.Failed && failed)
            || (mode == FoldMode.Changed && changed);
    }
}
