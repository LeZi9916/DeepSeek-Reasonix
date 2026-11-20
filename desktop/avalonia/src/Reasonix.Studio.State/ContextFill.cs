namespace Reasonix.Studio.State;

/// <summary>
/// How full the context window was on the last request, and how a usage frame
/// says it.
/// </summary>
/// <remarks>
/// The kernel reports two shapes in one frame: the billable totals, summed over
/// every attempt a round needed, and the latest single request's occupancy.
/// A context meter wants the second — summing attempts would draw a window
/// overflowing when one request fit comfortably — and falls back to the first
/// only because a legacy provider reports nothing else. This mirrors the
/// kernel's own <c>LatestPromptTokens</c>, so the two windows agree.
/// </remarks>
public static class ContextFill
{
    /// <summary>
    /// Reads the tokens the last request carried.
    /// </summary>
    /// <param name="contextPromptTokens">The latest request's prompt size, or 0 when unset.</param>
    /// <param name="promptTokens">The billable prompt total, used when the latest is unset.</param>
    /// <returns>The occupancy to draw, or 0 when the frame carried neither.</returns>
    public static long Of(long contextPromptTokens, long promptTokens)
    {
        return contextPromptTokens > 0 ? contextPromptTokens : promptTokens;
    }
}
