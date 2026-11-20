using Reasonix.Studio.Kernel.Client;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Session;

/// <summary>
/// The three reads a conversation needs from the kernel's HTTP surface.
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
    /// Opens a conversation the session directory holds.
    /// </summary>
    /// <param name="path">The transcript's path, as the listing reported it.</param>
    /// <param name="cancellationToken">Cancels the switch.</param>
    /// <returns>A task that completes once the kernel made it the open session.</returns>
    Task ResumeAsync(string path, CancellationToken cancellationToken = default);
}
