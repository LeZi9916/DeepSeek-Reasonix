using Reasonix.Studio.Contracts.Diagnostics;
using System;
using System.Collections.Generic;
using System.Globalization;
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
///
/// A refusal leaves as <see cref="KernelRefusal"/>, never as a bare status. The
/// kernel distinguishes a session another pane holds (409) from one that is gone
/// (404), too large to replay (413) and one whose path this request got wrong
/// (400); a window that received only "the request failed" could not tell a
/// reader which of those to act on.
/// </remarks>
public sealed partial class KernelClient : Session.IKernelClient, IDisposable
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
        Pages = new StudioPageClient(_http);
    }

    /// <inheritdoc/>
    public StudioPageClient Pages
    {
        get;
    }

    /// <summary>
    /// Reads the open session's status.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The status, or null when the kernel answered without one.</returns>
    public async Task<SessionStatus?> StatusAsync(CancellationToken cancellationToken = default)
    {
        var request = StudioPerformance.Timestamp();
        using var response = await _http.GetAsync("/status", cancellationToken).ConfigureAwait(false);
        StudioPerformance.Record("status.http-response", request);
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
        var decode = StudioPerformance.Timestamp();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var status = await JsonSerializer.DeserializeAsync<SessionStatus>(body, cancellationToken: cancellationToken).ConfigureAwait(false);
        StudioPerformance.Record("status.body-and-decode", decode);
        return status;
    }

    /// <summary>
    /// Reads the session's stored messages.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The messages in the order the session wrote them.</returns>
    public async Task<IReadOnlyList<HistoryMessage>> HistoryAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("/history", cancellationToken).ConfigureAwait(false);
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
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
            ["intent"] = request.Intent,
        });

        using var message = new HttpRequestMessage(HttpMethod.Post, "/inbox/items")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

        using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Answers one approval the kernel is waiting on.
    /// </summary>
    /// <param name="id">The approval the kernel named.</param>
    /// <param name="allow">Whether the call may run.</param>
    /// <param name="session">Whether the same answer applies for the rest of this session.</param>
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
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists the conversations the session directory holds.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The conversations, as the kernel orders them.</returns>
    public async Task<IReadOnlyList<SessionEntry>> SessionsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("/sessions", cancellationToken).ConfigureAwait(false);
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var entries = await JsonSerializer
            .DeserializeAsync<List<SessionEntry>>(body, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return entries ?? new List<SessionEntry>();
    }

    /// <summary>
    /// Starts a conversation with nothing in it, replacing the one on screen.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once the kernel made the new session current.</returns>
    /// <remarks>
    /// The kernel mints the transcript's path; nothing is written until the first
    /// line is sent. That is why a window that starts a session and closes it
    /// again leaves no empty file behind, and why the rail lists no row for it
    /// until something is said.
    /// </remarks>
    public async Task NewSessionAsync(CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/new")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };

        using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Copies the most recent part of a conversation into a new transcript.
    /// </summary>
    /// <param name="path">The transcript whose history is past the read limit.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The new transcript and how much of the original it left behind.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is empty.</exception>
    /// <remarks>
    /// The original file is never touched: this writes a new transcript holding
    /// the newest part, so the person can open the whole original elsewhere
    /// whenever they like.
    /// </remarks>
    public async Task<SessionTail> TailAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("a tail names the transcript it reads", nameof(path));
        }

        var payload = JsonSerializer.Serialize(new Dictionary<string, string> { ["path"] = path });
        using var message = new HttpRequestMessage(HttpMethod.Post, "/session/tail")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

        using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<SessionTail>(body, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? new SessionTail();
    }

    /// <summary>
    /// Opens a conversation the session directory holds.
    /// </summary>
    /// <param name="path">The transcript's path, as the listing reported it.</param>
    /// <param name="cancellationToken">Cancels the switch.</param>
    /// <returns>A task that completes once the kernel made it the open session.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is empty.</exception>
    /// <exception cref="KernelRefusal">
    /// Thrown when the kernel refused the switch. 404 means the transcript is gone,
    /// 409 that a turn owns the session, 413 that its history is past the replay
    /// limit, 403 that the path is outside the session directory and 400 that this
    /// request's path was not a transcript name. The status is the thing a caller
    /// branches on; <see cref="KernelRefusal.Code"/> refines it.
    /// </exception>
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
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Releases the pooled connections this client opened.
    /// </summary>
    public void Dispose()
    {
        _http.Dispose();
    }

    /// <summary>
    /// Reads one window of a call's output as the history carries it.
    /// </summary>
    /// <param name="callId">The call to read.</param>
    /// <param name="offset">Where the window starts, in bytes.</param>
    /// <param name="limit">How many bytes it may carry.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The window the kernel answered with.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="callId"/> is empty.</exception>
    /// <remarks>
    /// The id is a lookup key into what <c>/history</c> already projects, never a
    /// path: nothing here can name a file.
    /// </remarks>
    public async Task<ToolOutputPage> ToolOutputAsync(
        string callId,
        int offset = 0,
        int limit = 64 * 1024,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(callId))
        {
            throw new ArgumentException("a call's output is read by the id the kernel named", nameof(callId));
        }

        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"/history/tool/{Uri.EscapeDataString(callId)}?offset={offset}&limit={limit}");

        using var response = await _http.GetAsync(path, cancellationToken).ConfigureAwait(false);
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<ToolOutputPage>(body, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? new ToolOutputPage { CallId = callId };
    }

    /// <summary>
    /// Answers an unsuccessful response with the identity it carried.
    /// </summary>
    /// <param name="response">The answer to inspect.</param>
    /// <param name="cancellationToken">Cancels the body read.</param>
    /// <returns>A task that completes when the answer was successful.</returns>
    /// <exception cref="KernelRefusal">Thrown with the status, code and detail the kernel sent.</exception>
    internal static async Task EnsureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw await KernelRefusal.FromAsync(response, cancellationToken).ConfigureAwait(false);
    }
}
