using Reasonix.Studio.State.Transcript;
using System.Collections.Generic;

namespace Reasonix.Studio.State;

/// <summary>
/// Everything one conversation on screen is made of.
/// </summary>
/// <remarks>
/// Immutable on purpose: the reducer returns a new state per event, and the diff
/// between two states is what tells a view what to redraw. Mutable state would
/// make every reader a possible writer, which is how a transcript ends up
/// disagreeing with itself about what is running.
/// </remarks>
public sealed record SessionState
{
    /// <summary>
    /// Gets the rows the transcript draws, in order.
    /// </summary>
    public IReadOnlyList<TranscriptItem> Items { get; init; } = System.Array.Empty<TranscriptItem>();

    /// <summary>Gets the position of the latest streamed text row, within this revision.</summary>
    public int? StreamingRow
    {
        get; init;
    }

    /// <summary>Gets a value indicating whether the snapshot came from stored or replayed events.</summary>
    public bool IsRestored
    {
        get; init;
    }

    /// <summary>Gets the live phases reported by child-run trackers, keyed by call id.</summary>
    public IReadOnlyDictionary<string, string> SubagentPhases
    {
        get; init;
    } = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>(System.StringComparer.Ordinal));

    /// <summary>
    /// Gets the task list the kernel last reported.
    /// </summary>
    public IReadOnlyList<PlanStep> Plan { get; init; } = System.Array.Empty<PlanStep>();

    /// <summary>
    /// Gets how the last turn ended, cleared when the next one starts.
    /// </summary>
    /// <remarks>
    /// Terminal is a fact about the turn in front of you, not a record that one
    /// ever finished; without clearing it the tick from an hour ago would still be
    /// on screen over work that is running now.
    /// </remarks>
    public TurnTerminal? Terminal
    {
        get; init;
    }

    /// <summary>
    /// Gets the notices about the machine running this conversation rather than
    /// about the conversation itself.
    /// </summary>
    public IReadOnlyList<RuntimeNotice> Runtime { get; init; } = System.Array.Empty<RuntimeNotice>();

    /// <summary>
    /// Gets a value indicating whether a turn is running.
    /// </summary>
    public bool Running
    {
        get; init;
    }

    /// <summary>
    /// Gets what the status chip says the turn is doing, as a message key.
    /// </summary>
    public string Doing { get; init; } = "common.idle";

    /// <summary>Gets the current wait for a provider response.</summary>
    public ModelResponseWait? ResponseWait
    {
        get; init;
    }

    /// <summary>
    /// Gets the error the window is showing, if any.
    /// </summary>
    public string Error { get; init; } = string.Empty;

    /// <summary>
    /// Gets which read raised the error, so a later success of that read can take
    /// it back.
    /// </summary>
    public string ErrorFrom { get; init; } = string.Empty;

    /// <summary>
    /// Gets the token and cost totals for this session.
    /// </summary>
    public Metrics Metrics { get; init; } = new Metrics();

    /// <summary>
    /// Gets what this round has produced so far, estimated from the chunks.
    /// </summary>
    /// <remarks>
    /// Usage only lands when the round closes, so a live reading is an estimate
    /// and says so; the usage event replaces it with what was billed.
    /// </remarks>
    public long OutLive
    {
        get; init;
    }

    /// <summary>
    /// Gets how many context tokens the last request carried.
    /// </summary>
    /// <remarks>
    /// The window itself is the kernel's, read with the status; this is the half
    /// that moves during the conversation, so a meter draws both.
    /// </remarks>
    public long ContextUsed
    {
        get; init;
    }

    /// <summary>
    /// Gets the model the running turn was started on.
    /// </summary>
    /// <remarks>
    /// Recorded when the turn began rather than read off the composer, which moves
    /// on while the turn it started is still running.
    /// </remarks>
    public string? TurnModel
    {
        get; init;
    }

    /// <summary>
    /// Gets the session-log index of the message the live turn is about.
    /// </summary>
    /// <remarks>
    /// A second start naming the same one is the same open delivered twice — a
    /// duplicate, not a turn that ended without saying so.
    /// </remarks>
    public int? TurnMsgIndex
    {
        get; init;
    }

    /// <summary>
    /// Gets the rows whose first appearance has not been shown yet.
    /// </summary>
    /// <remarks>
    /// Presentation bookkeeping, deliberately beside the rows rather than on them:
    /// what a card is does not depend on whether its arrival has been shown. The
    /// debt is carried rather than cleared per event, because several events can
    /// land between two renders and the one never drawn still owes its entrance.
    /// </remarks>
    public IReadOnlyList<string> EntranceOwed { get; init; } = System.Array.Empty<string>();

    /// <summary>
    /// Gets how far this projection has already let rows in.
    /// </summary>
    public long EnteredThrough
    {
        get; init;
    }

    /// <summary>
    /// Gets the counter bumped when the transcript's composition changes.
    /// </summary>
    /// <remarks>
    /// A message still being written growing by a chunk does not bump it: the
    /// derivations that read the rows' shapes can key off this and skip the
    /// hundreds of frames one answer streams in. A derivation that reads a row's
    /// text must key off the rows instead.
    /// </remarks>
    public long Revision
    {
        get; init;
    }

    /// <summary>
    /// Gets when this window saw the turn start, in unix milliseconds.
    /// </summary>
    /// <remarks>
    /// Unknown for a turn that began before this window attached.
    /// </remarks>
    public long? RunSince
    {
        get; init;
    }
}
