using Reasonix.Studio.Contracts.Tree;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

// The writes the sidebar makes to the kernel's tree: a conversation pinned,
// archived or marked seen.
//
// Each answers with no body and the change reaches every window as a tree
// event, so the caller re-reads the tree rather than patching its own copy. That
// is what keeps two windows showing one answer, and it is also why nothing here
// returns the row it changed.
//
// The type's documentation lives beside its first declaration in KernelClient.cs.
public sealed partial class KernelClient
{
    /// <summary>
    /// Pins or unpins one conversation.
    /// </summary>
    /// <param name="path">The transcript's path, as the tree listed it.</param>
    /// <param name="pinned">True to pin, false to unpin.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once the kernel recorded the pin.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is empty.</exception>
    public async Task PinSessionAsync(string path, bool pinned, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["path"] = path,
            ["pinned"] = pinned,
        });

        await PostNoContentAsync("/tree/sessions/pin", payload, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Archives or unarchives one conversation.
    /// </summary>
    /// <param name="path">The transcript's path, as the tree listed it.</param>
    /// <param name="archived">True to archive, false to bring it back.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once the kernel recorded the change.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is empty.</exception>
    /// <remarks>
    /// A conversation a pane is running is refused with a conflict rather than
    /// archived under the turn: the kernel answers which pane holds it, and the
    /// window asks the person to stop it first.
    /// </remarks>
    public async Task ArchiveSessionAsync(string path, bool archived, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["path"] = path,
            ["archived"] = archived,
        });

        await PostNoContentAsync("/tree/sessions/archive", payload, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Marks one conversation seen, which clears its unread dot.
    /// </summary>
    /// <param name="path">The transcript's path, as the tree listed it.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once the kernel recorded the moment.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is empty.</exception>
    public async Task MarkSessionViewedAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var payload = JsonSerializer.Serialize(new Dictionary<string, string> { ["path"] = path });
        await PostNoContentAsync("/tree/sessions/viewed", payload, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Posts one body to a route that answers with no content.
    /// </summary>
    /// <param name="path">The route to post to.</param>
    /// <param name="payload">The JSON body, already serialised.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once the kernel accepted the body.</returns>
    private async Task PostNoContentAsync(string path, string payload, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

        using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
    }
}
