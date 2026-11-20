using Reasonix.Studio.Contracts.Events;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Events;

/// <summary>
/// Carries the kernel's event stream over HTTP and SSE.
/// </summary>
/// <remarks>
/// Reconnection lives here rather than in the subscription because resuming is a
/// transport fact: the same cursor is asked for again on the wire, and the frames
/// that come back are deduplicated by <see cref="EventSubscription"/>. Silence is
/// the failure this handles that a dropped socket is not — a phone that slept or
/// a link that went away leaves the connection looking open, so the stream is
/// replaced once it has said nothing for
/// <see cref="SilenceThreshold"/>.
/// </remarks>
public sealed class SseEventTransport : IEventTransport
{
    /// <summary>
    /// How long a stream may say nothing before it is treated as gone.
    /// </summary>
    /// <remarks>
    /// The kernel restates its position every fifteen seconds, so three silent
    /// intervals is a stream that is gone whatever the socket says.
    /// </remarks>
    public static readonly TimeSpan SilenceThreshold = TimeSpan.FromSeconds(45);

    /// <summary>
    /// The path the live stream is served from.
    /// </summary>
    private static readonly string s_streamPath = "/events";

    /// <summary>
    /// The path the bounded replay log is read from.
    /// </summary>
    private static readonly string s_replayPath = "/events/replay";

    /// <summary>
    /// How often a live stream is checked for silence.
    /// </summary>
    private static readonly TimeSpan s_checkInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The client every read shares, so connections are pooled rather than rebuilt.
    /// </summary>
    private readonly HttpClient _http;

    /// <summary>
    /// The kernel's address, without a trailing slash.
    /// </summary>
    private readonly string _base;

    /// <summary>
    /// Initializes a new instance of the <see cref="SseEventTransport"/> class.
    /// </summary>
    /// <param name="baseAddress">The kernel's origin, such as <c>http://127.0.0.1:7411</c>.</param>
    /// <param name="http">The client to read through, or null to create one.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="baseAddress"/> is null.</exception>
    public SseEventTransport(string baseAddress, HttpClient? http = null)
    {
        if (baseAddress is null)
        {
            throw new ArgumentNullException(nameof(baseAddress));
        }

        _base = baseAddress.TrimEnd('/');
        _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<WireFrame> StreamAsync(
        Func<long?> cursor,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var after = cursor();
            var url = after is { } seq ? $"{_base}{s_streamPath}?lastEventId={seq}" : $"{_base}{s_streamPath}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("text/event-stream");

            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(body, Encoding.UTF8);
            using var silence = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            string? pending = null;
            var lastHeard = DateTimeOffset.UtcNow;
            using var check = new Timer(
                _ =>
                {
                    if (DateTimeOffset.UtcNow - lastHeard > SilenceThreshold)
                    {
                        // The socket still looks open. Disposing the reader is what
                        // actually ends the read, so the loop reconnects from the
                        // cursor the subscription now holds.
                        silence.Cancel();
                    }
                },
                null,
                s_checkInterval,
                s_checkInterval);

            while (true)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync(silence.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (line is null)
                {
                    break;
                }

                lastHeard = DateTimeOffset.UtcNow;

                if (line.Length == 0)
                {
                    // A blank line ends one frame; the data lines since the last
                    // one are its body.
                    if (pending is not null && WireFrame.TryParse(pending, out var frame))
                    {
                        yield return frame;
                    }

                    pending = null;
                    continue;
                }

                if (line.StartsWith(':'))
                {
                    // A keepalive comment: it reached us, which is all it is for.
                    continue;
                }

                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    var chunk = line.AsSpan(5).TrimStart(' ').ToString();
                    pending = pending is null ? chunk : pending + "\n" + chunk;
                }
            }
        }
    }

    /// <inheritdoc/>
    public async Task<ReplayResult> ReplayAsync(long after, CancellationToken cancellationToken)
    {
        var url = $"{_base}{s_replayPath}?lastEventId={after}";
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(body, default, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        var frames = new List<WireFrame>();
        if (root.TryGetProperty("frames", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in list.EnumerateArray())
            {
                if (WireFrame.TryParse(element.GetRawText(), out var frame))
                {
                    frames.Add(frame);
                }
            }
        }

        // A replay that cannot say it is complete is treated as incomplete: the
        // caller re-reads a snapshot instead of trusting a hole it cannot see.
        var complete = root.TryGetProperty("complete", out var flag)
            && flag.ValueKind == JsonValueKind.True;
        var watermark = 0L;
        if (root.TryGetProperty("watermark", out var mark) && mark.ValueKind == JsonValueKind.Number)
        {
            watermark = mark.TryGetInt64(out var parsed) ? parsed : 0L;
        }

        return new ReplayResult(frames, complete, watermark);
    }

    /// <summary>
    /// Releases the pooled connections this transport opened.
    /// </summary>
    public void Dispose()
    {
        _http.Dispose();
    }
}
