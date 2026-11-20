using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>
/// Reads the session over the kernel's HTTP surface.
/// </summary>
/// <remarks>
/// Every call goes through one <see cref="HttpClient"/>, which carries the launch
/// credential and refuses to send it anywhere but this launch's listener. The
/// client owns no state of its own: a read is a read, and what a window remembers
/// belongs to the reducer.
/// </remarks>
public sealed class KernelClient : Session.IKernelClient, IDisposable
{
    /// <summary>
    /// The client every call shares, so connections are pooled and the credential
    /// rides one handler.
    /// </summary>
    private readonly HttpClient _http;

    /// <summary>
    /// Initializes a new instance of the <see cref="KernelClient"/> class.
    /// </summary>
    /// <param name="http">The client to read through, already carrying the credential.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="http"/> is null.</exception>
    public KernelClient(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <summary>
    /// Reads the open session's status.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The status, or null when the kernel answered without one.</returns>
    public async Task<SessionStatus?> StatusAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("/status", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<SessionStatus>(body, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the session's stored messages.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The messages in the order the session wrote them.</returns>
    public async Task<IReadOnlyList<HistoryMessage>> HistoryAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("/history", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var messages = await JsonSerializer
            .DeserializeAsync<List<HistoryMessage>>(body, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return messages ?? new List<HistoryMessage>();
    }

    /// <summary>
    /// Starts a turn from one typed line.
    /// </summary>
    /// <param name="request">The line to send.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>A task that completes once the kernel accepted the line.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
    public async Task SubmitAsync(SubmitRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        // The inbox is the one entrance a turn has, whether it is typed or queued:
        // going through it is what makes a submitted line carry the same envelope
        // a chip line does, so the turn sees one shape rather than two.
        var payload = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["input"] = request.Input,
            ["submit"] = request.Submit,
            ["intent"] = "followup",
        });

        using var message = new HttpRequestMessage(HttpMethod.Post, "/inbox/items")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

        using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Answers one approval the kernel is waiting on.
    /// </summary>
    /// <param name="id">The approval the kernel named.</param>
    /// <param name="allow">Whether the call may run.</param>
    /// <param name="session">Whether the same answer applies for the rest of the session.</param>
    /// <param name="persist">Whether the answer is remembered beyond this session.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>A task that completes once the kernel accepted the verdict.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> is empty.</exception>
    public async Task ApproveAsync(
        string id,
        bool allow,
        bool session,
        bool persist,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new ArgumentException("an approval is answered by the id the kernel named", nameof(id));
        }

        // The two scope flags travel separately because they answer different
        // questions: session widens this conversation, persist widens every later
        // one, and the kernel decides what each may write.
        var payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["id"] = id,
            ["allow"] = allow,
            ["session"] = session,
            ["persist"] = persist,
        });

        using var message = new HttpRequestMessage(HttpMethod.Post, "/approve")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

        using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Lists the conversations the session directory holds.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The conversations, as the kernel orders them.</returns>
    public async Task<IReadOnlyList<SessionEntry>> SessionsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("/sessions", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var entries = await JsonSerializer
            .DeserializeAsync<List<SessionEntry>>(body, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return entries ?? new List<SessionEntry>();
    }

    /// <summary>
    /// Opens a conversation the session directory holds.
    /// </summary>
    /// <param name="path">The transcript's path, as the listing reported it.</param>
    /// <param name="cancellationToken">Cancels the switch.</param>
    /// <returns>A task that completes once the kernel made it the open session.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is empty.</exception>
    public async Task ResumeAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("a resume names the transcript it opens", nameof(path));
        }

        var payload = JsonSerializer.Serialize(new Dictionary<string, string> { ["path"] = path });
        using var message = new HttpRequestMessage(HttpMethod.Post, "/resume")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

        using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Releases the pooled connections this client opened.
    /// </summary>
    public void Dispose()
    {
        _http.Dispose();
    }
}
