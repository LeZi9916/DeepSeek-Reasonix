using Reasonix.Studio.Contracts.Queue;
using Reasonix.Studio.Kernel.Client;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Session;

/// <summary>
/// The reads and writes one conversation needs from the kernel's HTTP surface.
/// </summary>
/// <remarks>
/// An interface so the conversation's assembly can be held by a test without a
/// socket: what is worth proving is that a frame reaching the subscription becomes
/// state, and that a reopened session opens on its transcript, neither of which
/// depends on a listener existing.
/// </remarks>
public interface IKernelClient
{
    /// <summary>
    /// Reads the open session's status.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The status, or null when the kernel answered without one.</returns>
    Task<SessionStatus?> StatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the session's stored messages.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The messages in the order the session wrote them.</returns>
    Task<IReadOnlyList<HistoryMessage>> HistoryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a turn from one typed line.
    /// </summary>
    /// <param name="request">The line to send.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>A task that completes once the kernel accepted the line.</returns>
    Task SubmitAsync(SubmitRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers one approval the kernel is waiting on.
    /// </summary>
    /// <param name="id">The approval the kernel named.</param>
    /// <param name="allow">Whether the call may run.</param>
    /// <param name="session">Whether the same answer applies for the rest of the session.</param>
    /// <param name="persist">Whether the answer is remembered beyond this session.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>A task that completes once the kernel accepted the verdict.</returns>
    Task ApproveAsync(string id, bool allow, bool session, bool persist, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the conversations the session directory holds.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The conversations, as the kernel orders them.</returns>
    Task<IReadOnlyList<SessionEntry>> SessionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the sidebar's tree: every project and the conversations under it.
    /// </summary>
    /// <param name="window">The page to ask for, or null for the default window.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The projects, as the kernel orders them.</returns>
    Task<IReadOnlyList<Reasonix.Studio.Contracts.Tree.TreeWorkspace>> TreeAsync(
        Reasonix.Studio.Contracts.Tree.TreeWindow? window = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pins or unpins one conversation.
    /// </summary>
    /// <param name="path">The transcript's path, as the tree listed it.</param>
    /// <param name="pinned">True to pin, false to unpin.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once the kernel recorded the pin.</returns>
    Task PinSessionAsync(string path, bool pinned, CancellationToken cancellationToken = default);

    /// <summary>
    /// Archives or unarchives one conversation.
    /// </summary>
    /// <param name="path">The transcript's path, as the tree listed it.</param>
    /// <param name="archived">True to archive, false to bring it back.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once the kernel recorded the change.</returns>
    Task ArchiveSessionAsync(string path, bool archived, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks one conversation seen, which clears its unread dot.
    /// </summary>
    /// <param name="path">The transcript's path, as the tree listed it.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once the kernel recorded the moment.</returns>
    Task MarkSessionViewedAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a conversation with nothing in it, replacing the one on screen.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once the kernel made the new session current.</returns>
    Task NewSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies the most recent part of a conversation into a new transcript.
    /// </summary>
    /// <param name="path">The transcript whose history is past the read limit.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The new transcript and how much of the original it left behind.</returns>
    Task<SessionTail> TailAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a conversation the session directory holds.
    /// </summary>
    /// <param name="path">The transcript's path, as the listing reported it.</param>
    /// <param name="cancellationToken">Cancels the switch.</param>
    /// <returns>A task that completes once the kernel made it the open session.</returns>
    Task ResumeAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one window of a call's output.
    /// </summary>
    /// <param name="callId">The call to read.</param>
    /// <param name="offset">Where the window starts, in bytes.</param>
    /// <param name="limit">How many bytes it may carry.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The window the kernel answered with.</returns>
    Task<ToolOutputPage> ToolOutputAsync(string callId, int offset = 0, int limit = 64 * 1024, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the session's queue as the kernel holds it.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Every waiting entry, with the pause and both limits.</returns>
    Task<QueueSnapshot> QueueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Admits one line into the queue, as a follow-up or as guidance.
    /// </summary>
    /// <param name="request">The line, what the model reads, and what it becomes.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>The receipt naming the durable entry.</returns>
    Task<InboxReceipt> EnqueueAsync(SubmitRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one queued entry whole, including the text it carries.
    /// </summary>
    /// <param name="id">The entry the queue listed.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The entry's metadata and its text.</returns>
    Task<QueueEntry> ReadQueuedAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes one entry out of the queue.
    /// </summary>
    /// <param name="id">The entry to remove.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once the kernel dropped it.</returns>
    Task CancelQueuedAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves one entry to a position in the queue.
    /// </summary>
    /// <param name="id">The entry to move.</param>
    /// <param name="toIndex">The position it takes, counting from the head.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once the kernel moved it.</returns>
    Task MoveQueuedAsync(string id, int toIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the kernel to retry a blocked entry.
    /// </summary>
    /// <param name="id">The entry to retry.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once the kernel accepted the retry.</returns>
    Task RetryQueuedAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Holds the queue, or lets it dispatch again.
    /// </summary>
    /// <param name="paused">True to hold dispatch, false to release it.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once the kernel recorded the state.</returns>
    Task SetQueuePausedAsync(bool paused, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the turn that is running.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once the kernel accepted the stop.</returns>
    Task CancelTurnAsync(CancellationToken cancellationToken = default);
}
