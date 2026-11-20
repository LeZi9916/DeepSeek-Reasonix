using Reasonix.Studio.Kernel.Boundary;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Events;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Drives the conversation over a real socket, against a listener that enforces the
/// same three checks the kernel's gate does.
/// </summary>
/// <remarks>
/// The unit cases hold each rule on its own. This one holds the assembly: a
/// credentialled client, a real HTTP exchange, a real SSE stream and the reducer,
/// meeting where they actually meet. A fake transport in memory cannot notice that
/// the cookie header is spelled wrong or that the stream parser splits frames at the
/// wrong line, and those are exactly the defects a migration would ship.
/// </remarks>
public sealed class LoopbackIntegrationTests
{
    /// <summary>
    /// The credential the case's kernel accepts.
    /// </summary>
    private const string Token = "9f2c1c0e5b7a4d3e8f6a1b2c3d4e5f60718293a4b5c6d7e8f9012345678abcde";

    /// <summary>
    /// Checks that the status, the history and the live stream all arrive over one
    /// credentialled client, and that a frame from the stream becomes a row.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task AConversationRunsOverARealSocket()
    {
        await using var kernel = new FakeKernel(Token);
        kernel.ServeStatus("{\"label\":\"checkout\",\"running\":false,\"used\":1024,\"window\":65536}");
        kernel.ServeHistory("[{\"role\":\"user\",\"content\":\"question\",\"msgIndex\":0},{\"role\":\"assistant\",\"content\":\"answer\",\"msgIndex\":1}]");
        kernel.ServeEvents(
            "id: 1\ndata: {\"kind\":\"text\",\"text\":\"live \",\"seq\":1}\n\n",
            "id: 2\ndata: {\"kind\":\"text\",\"text\":\"answer\",\"seq\":2}\n\n");

        var origin = new Uri(kernel.Address);
        var cookie = new LaunchCookie(origin, Token);
        using var http = new HttpClient(new CredentialHandler(cookie))
        {
            BaseAddress = origin,
            Timeout = Timeout.InfiniteTimeSpan,
        };

        await using var engine = new SessionEngine(
            new KernelClient(http),
            new SseEventTransport(origin.ToString(), http));

        var status = await engine.StatusAsync();
        Assert.NotNull(status);
        Assert.Equal("checkout", status!.Label);
        Assert.Equal(65536, status.Window);

        await engine.LoadHistoryAsync();
        Assert.Equal(2, engine.State.Items.Count);

        // The stream is a long-lived pump by design: awaiting it here would be
        // waiting for the conversation to end, which it does when the engine is
        // disposed. So it is started, the frames are waited for, and disposing
        // closes it.
        var pumping = engine.StartAsync();
        var arrived = await WaitForAsync(() => engine.State.Items.OfType<SayItem>().Count() >= 2);
        await engine.DisposeAsync();
        await pumping;

        Assert.True(arrived, "the two live frames never reached the transcript");

        var said = engine.State.Items.OfType<SayItem>().ToList();
        Assert.Equal(2, said.Count);
        Assert.Equal("question", Assert.IsType<UserItem>(engine.State.Items[0]).Text);
        Assert.Equal("answer", said[0].Text);
        Assert.Equal("live answer", said[1].Text);

        // Every request the kernel saw was addressed to it, carried the credential,
        // and named its origin when it changed state.
        Assert.All(kernel.Requests, request => Assert.Equal("reasonix_token=" + Token, request.Cookie));
        Assert.All(kernel.Requests, request => Assert.Equal(kernel.Authority, request.Host));
        Assert.Equal(0, kernel.Refusals);
    }

    /// <summary>
    /// Checks that the client refuses to carry the credential to a second listener,
    /// which is the leak the boundary exists to prevent.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task TheCredentialNeverReachesASecondListener()
    {
        await using var kernel = new FakeKernel(Token);
        kernel.ServeStatus("{\"label\":\"x\"}");

        var origin = new Uri(kernel.Address);
        using var http = new HttpClient(new CredentialHandler(new LaunchCookie(origin, Token)))
        {
            BaseAddress = origin,
        };

        // Same host, different port: a different listener, so a different origin.
        var elsewhere = new UriBuilder(origin) { Port = origin.Port + 1 }.Uri;
        await Assert.ThrowsAsync<InvalidOperationException>(() => http.GetAsync(elsewhere));

        // Nothing was sent, so the kernel saw no request at all.
        Assert.Empty(kernel.Requests);
    }

    /// <summary>
    /// Waits until a condition holds, so a case times out on what it is waiting for
    /// rather than on the test host's own deadline.
    /// </summary>
    /// <param name="condition">The condition to wait for.</param>
    /// <returns>True when the condition held within the budget; false when it did not.</returns>
    private static async Task<bool> WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return condition();
    }

    /// <summary>
    /// Reads the resume cursor a stream request carries.
    /// </summary>
    /// <param name="path">The request's path, with any query.</param>
    /// <returns>The last event id the client already saw, or 0 on an attach.</returns>
    private static long LastEventIdOf(string path)
    {
        var at = path.IndexOf("lastEventId=", StringComparison.Ordinal);
        if (at < 0)
        {
            return 0;
        }

        var start = at + "lastEventId=".Length;
        var end = path.IndexOf('&', start);
        var text = end < 0 ? path[start..] : path[start..end];
        return long.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var seq) ? seq : 0;
    }

    /// <summary>
    /// Reads the frame number an SSE block states on its <c>id:</c> line.
    /// </summary>
    /// <param name="frame">The block, already written as SSE text.</param>
    /// <returns>The number, or null when the block carries none.</returns>
    private static long? SeqOf(string frame)
    {
        foreach (var line in frame.Split('\n'))
        {
            if (line.StartsWith("id:", StringComparison.Ordinal))
            {
                if (!long.TryParse(
                    line[3..].Trim(),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var seq))
                {
                    return null;
                }

                return seq;
            }
        }

        return null;
    }

    /// <summary>
    /// A minimal HTTP listener that enforces the gate's three checks and answers the
    /// three reads a conversation makes.
    /// </summary>
    private sealed class FakeKernel : IAsyncDisposable
    {
        /// <summary>
        /// The socket the kernel listens on.
        /// </summary>
        private readonly TcpListener _listener;

        /// <summary>
        /// Cancels the accept loop.
        /// </summary>
        private readonly CancellationTokenSource _stopping = new CancellationTokenSource();

        /// <summary>
        /// The accept loop.
        /// </summary>
        private readonly Task _serving;

        /// <summary>
        /// The credential this listener accepts.
        /// </summary>
        private readonly string _token;

        /// <summary>
        /// What a status read answers.
        /// </summary>
        private string _status = "{}";

        /// <summary>
        /// What a history read answers.
        /// </summary>
        private string _history = "[]";

        /// <summary>
        /// The SSE frames a stream answers with, in order.
        /// </summary>
        private string[] _events = Array.Empty<string>();

        /// <summary>
        /// Initializes a new instance of the <see cref="FakeKernel"/> class.
        /// </summary>
        /// <param name="token">The credential this listener accepts.</param>
        public FakeKernel(string token)
        {
            _token = token;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Address = $"http://127.0.0.1:{port}";
            Authority = $"127.0.0.1:{port}";
            _serving = AcceptAsync();
        }

        /// <summary>
        /// Gets the origin this listener answers for.
        /// </summary>
        public string Address
        {
            get;
        }

        /// <summary>
        /// Gets the authority the gate requires in the Host header.
        /// </summary>
        public string Authority
        {
            get;
        }

        /// <summary>
        /// Gets every request this listener admitted, in order.
        /// </summary>
        public List<Seen> Requests { get; } = new List<Seen>();

        /// <summary>
        /// Gets how many requests the gate refused.
        /// </summary>
        public int Refusals
        {
            get; private set;
        }

        /// <summary>
        /// Sets what a status read answers.
        /// </summary>
        /// <param name="json">The body.</param>
        public void ServeStatus(string json) => _status = json;

        /// <summary>
        /// Sets what a history read answers.
        /// </summary>
        /// <param name="json">The body.</param>
        public void ServeHistory(string json) => _history = json;

        /// <summary>
        /// Sets the frames a stream answers with.
        /// </summary>
        /// <param name="frames">The frames, already written as SSE blocks.</param>
        public void ServeEvents(params string[] frames) => _events = frames;

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await _stopping.CancelAsync().ConfigureAwait(false);
            _listener.Stop();
            try
            {
                await _serving.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The listener closing is the outcome asked for.
            }

            _stopping.Dispose();
        }

        /// <summary>
        /// Writes one JSON response and closes the connection.
        /// </summary>
        /// <param name="stream">The connection's stream.</param>
        /// <param name="status">The status line.</param>
        /// <param name="type">The content type.</param>
        /// <param name="body">The body.</param>
        /// <returns>A task that completes once the response is written.</returns>
        private static async Task RespondAsync(NetworkStream stream, string status, string type, string body)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            var head = $"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(head)).ConfigureAwait(false);
            await stream.WriteAsync(bytes).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Accepts connections until the listener stops.
        /// </summary>
        /// <returns>A task that completes when the listener stops.</returns>
        private async Task AcceptAsync()
        {
            while (!_stopping.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_stopping.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }

                _ = HandleAsync(client);
            }
        }

        /// <summary>
        /// Reads one request, applies the gate, and answers it.
        /// </summary>
        /// <param name="client">The connection.</param>
        /// <returns>A task that completes once the connection is closed.</returns>
        private async Task HandleAsync(TcpClient client)
        {
            using (client)
            {
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, false, 8192, leaveOpen: true);

                var requestLine = await reader.ReadLineAsync().ConfigureAwait(false);
                if (requestLine is null)
                {
                    return;
                }

                var parts = requestLine.Split(' ');
                var method = parts[0];
                var path = parts.Length > 1 ? parts[1] : "/";

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync().ConfigureAwait(false)))
                {
                    var colon = line.IndexOf(':');
                    if (colon > 0)
                    {
                        headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
                    }
                }

                var cookie = headers.TryGetValue("Cookie", out var raw) ? raw : null;
                var host = headers.TryGetValue("Host", out var h) ? h : null;
                var origin = headers.TryGetValue("Origin", out var o) ? o : null;

                // The gate's order, outermost first: address, then origin, then
                // credential. A caller that reached the socket under another name
                // learns nothing about the token.
                if (!string.Equals(host, Authority, StringComparison.OrdinalIgnoreCase))
                {
                    Refusals++;
                    await RespondAsync(stream, "403 Forbidden", "application/json", "{}").ConfigureAwait(false);
                    return;
                }

                var changesState = method != "GET" && method != "HEAD";
                if (changesState && !string.Equals(origin, Address, StringComparison.Ordinal))
                {
                    Refusals++;
                    await RespondAsync(stream, "403 Forbidden", "application/json", "{}").ConfigureAwait(false);
                    return;
                }

                if (!string.Equals(cookie, "reasonix_token=" + _token, StringComparison.Ordinal))
                {
                    Refusals++;
                    await RespondAsync(stream, "403 Forbidden", "application/json", "{}").ConfigureAwait(false);
                    return;
                }

                string body;
                string type;
                string status;

                if (path.StartsWith("/status", StringComparison.Ordinal))
                {
                    status = "200 OK";
                    type = "application/json";
                    body = _status;
                }
                else if (path.StartsWith("/history", StringComparison.Ordinal))
                {
                    status = "200 OK";
                    type = "application/json";
                    body = _history;
                }
                else if (path.StartsWith("/events/replay", StringComparison.Ordinal))
                {
                    status = "200 OK";
                    type = "application/json";
                    body = "{\"frames\":[],\"complete\":true,\"watermark\":2}";
                }
                else if (path.StartsWith("/events", StringComparison.Ordinal))
                {
                    Requests.Add(new Seen(host!, cookie, origin, path));

                    // A real stream resumes where the client left off: the query
                    // carries the last id it saw, and frames at or before it are not
                    // sent again. Replaying them instead reads, on the client, as
                    // numbering that went backwards — a restarted kernel — which is
                    // how a fake that ignores the cursor manufactures duplicates.
                    var after = LastEventIdOf(path);
                    var head = "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nCache-Control: no-cache\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(head)).ConfigureAwait(false);
                    foreach (var frame in _events)
                    {
                        if (SeqOf(frame) is { } seq && seq <= after)
                        {
                            continue;
                        }

                        await stream.WriteAsync(Encoding.UTF8.GetBytes(frame)).ConfigureAwait(false);
                        await stream.FlushAsync().ConfigureAwait(false);
                    }

                    return;
                }
                else
                {
                    status = "404 Not Found";
                    type = "application/json";
                    body = "{}";
                }

                Requests.Add(new Seen(host!, cookie, origin, path));
                await RespondAsync(stream, status, type, body).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// What one admitted request carried at the boundary headers.
    /// </summary>
    /// <param name="Host">The Host header.</param>
    /// <param name="Cookie">The Cookie header, or null.</param>
    /// <param name="Origin">The Origin header, or null.</param>
    /// <param name="Path">The path asked for.</param>
    private sealed record Seen(string Host, string? Cookie, string? Origin, string Path);
}
