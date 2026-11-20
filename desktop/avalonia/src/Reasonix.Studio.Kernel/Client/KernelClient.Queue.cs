using Reasonix.Studio.Contracts.Queue;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

// The session's outbox, over the kernel's /inbox routes.
//
// Every edit is asked of the kernel and the whole snapshot is read back, rather
// than patched here: the queue is shared — another window, the CLI and the
// kernel's own continuations write to it — so a local guess would disagree with
// what will actually run. The read-back is also what puts those other lines in
// front of this window.
//
// The type's documentation lives beside its first declaration in KernelClient.cs.
public sealed partial class KernelClient
{
    /// <summary>
    /// Reads the queue as the kernel holds it.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Every waiting entry, with both limits in force.</returns>
    public async Task<QueueSnapshot> QueueAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("/inbox", cancellationToken).ConfigureAwait(false);
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<QueueSnapshot>(body, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? new QueueSnapshot();
    }

    /// <summary>
    /// Admits one line into the session's queue.
    /// </summary>
    /// <param name="request">The line, what the model reads, and what it becomes.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>The receipt, whose item id is what a later withdrawal names.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
    public async Task<InboxReceipt> EnqueueAsync(SubmitRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var payload = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["input"] = request.Input,
            ["submit"] = request.Submit,
            ["intent"] = request.Intent,
        });

        using var message = new HttpRequestMessage(HttpMethod.Post, "/inbox/items")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

        using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<InboxReceipt>(body, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? new InboxReceipt();
    }

    /// <summary>
    /// Reads one queued entry whole, including the text it carries.
    /// </summary>
    /// <param name="id">The entry the queue listed.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The entry's metadata and its text.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> is empty.</exception>
    /// <remarks>
    /// This read comes before any withdrawal: the manifest keeps only a preview,
    /// so cancelling first would leave the text nowhere to go.
    /// </remarks>
    public async Task<QueueEntry> ReadQueuedAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        var path = "/inbox/items/" + Uri.EscapeDataString(id);
        using var response = await _http.GetAsync(path, cancellationToken).ConfigureAwait(false);
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<QueueEntry>(body, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? new QueueEntry();
    }

    /// <summary>
    /// Takes one entry out of the queue.
    /// </summary>
    /// <param name="id">The entry to remove.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once the kernel dropped it.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> is empty.</exception>
    public async Task CancelQueuedAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        var path = "/inbox/items/" + Uri.EscapeDataString(id);
        using var response = await _http.DeleteAsync(path, cancellationToken).ConfigureAwait(false);
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Moves one entry to a position in the queue.
    /// </summary>
    /// <param name="id">The entry to move.</param>
    /// <param name="toIndex">The position it takes, counting from the head.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once the kernel moved it.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> is empty.</exception>
    public async Task MoveQueuedAsync(string id, int toIndex, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        var payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["id"] = id,
            ["toIndex"] = toIndex,
        });

        using var message = new HttpRequestMessage(HttpMethod.Post, "/inbox/move")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

        using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Holds the queue, or lets it dispatch again.
    /// </summary>
    /// <param name="paused">True to hold dispatch, false to release it.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once the kernel recorded the state.</returns>
    public async Task SetQueuePausedAsync(bool paused, CancellationToken cancellationToken = default)
    {
        var path = paused ? "/inbox/pause" : "/inbox/resume";
        using var message = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };

        using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asks the kernel to retry a blocked entry.
    /// </summary>
    /// <param name="id">The entry to retry.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once the kernel accepted the retry.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> is empty.</exception>
    public async Task RetryQueuedAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        var path = "/inbox/items/" + Uri.EscapeDataString(id) + "/retry";
        using var message = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };

        using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops the turn that is running.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once the kernel accepted the stop.</returns>
    /// <remarks>
    /// What "send now" uses to make room at the head of the queue; on its own it
    /// is the escape a reader has while a turn will not finish.
    /// </remarks>
    public async Task CancelTurnAsync(CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/cancel")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };

        using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
    }
}
