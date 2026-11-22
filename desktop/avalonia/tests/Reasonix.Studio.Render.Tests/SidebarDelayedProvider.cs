using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Render.Tests;

/// <summary>A loopback-only OpenAI-compatible endpoint with explicit streaming checkpoints.</summary>
internal sealed class SidebarDelayedProvider : IAsyncDisposable
{
    /// <summary>The test-owned listener, never an external provider.</summary>
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    /// <summary>Cancels every test-owned connection.</summary>
    private readonly CancellationTokenSource _closing = new();

    /// <summary>The accepted connection lifetimes.</summary>
    private readonly List<Task> _connections = [];

    /// <summary>The loop accepting test provider requests.</summary>
    private readonly Task _accepting;

    /// <summary>The request count for the controlled main model.</summary>
    private int _slowRequests;

    /// <summary>The last unexpected local protocol failure.</summary>
    private string _failure = string.Empty;

    /// <summary>Initializes a new instance of the <see cref="SidebarDelayedProvider"/> class.</summary>
    internal SidebarDelayedProvider()
    {
        _listener.Start();
        Origin = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/v1";
        _accepting = AcceptAsync();
    }

    /// <summary>Gets the loopback model endpoint.</summary>
    internal string Origin
    {
        get;
    }

    /// <summary>Gets the first chunk acknowledgement.</summary>
    internal TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets the explicit permission to send the second chunk.</summary>
    internal TaskCompletionSource<bool> Advance { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets the second chunk acknowledgement.</summary>
    internal TaskCompletionSource<bool> Advanced { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets the explicit permission to complete generation.</summary>
    internal TaskCompletionSource<bool> Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets the main-model request count.</summary>
    internal int SlowRequests => Volatile.Read(ref _slowRequests);

    /// <summary>Gets unexpected protocol diagnostics without storing request credentials.</summary>
    internal string Failure => Volatile.Read(ref _failure);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        Advance.TrySetResult(true);
        Finish.TrySetResult(true);
        _closing.Cancel();
        _listener.Stop();
        await _accepting.ConfigureAwait(false);
        await Task.WhenAll(_connections).ConfigureAwait(false);
        _closing.Dispose();
    }

    /// <summary>Accepts independent metadata and generation calls on the same local endpoint.</summary>
    /// <returns>The listener lifetime.</returns>
    private async Task AcceptAsync()
    {
        try
        {
            while (!_closing.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_closing.Token).ConfigureAwait(false);
                _connections.Add(ServeAsync(client));
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
        catch (SocketException) when (_closing.IsCancellationRequested) { }
    }

    /// <summary>Reads HTTP framing and streams deterministic provider text through the real kernel.</summary>
    /// <param name="client">The accepted loopback socket.</param><returns>The response lifetime.</returns>
    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                var body = await RequestBodyAsync(stream).ConfigureAwait(false);
                var model = (string?)JsonNode.Parse(body)?["model"] ?? string.Empty;
                await WriteAsync(stream, "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nConnection: close\r\n\r\n").ConfigureAwait(false);
                var controlled = model == "slow" && Interlocked.Increment(ref _slowRequests) == 1;
                if (controlled)
                {
                    await ChunkAsync(stream, "first segment ").ConfigureAwait(false);
                    Started.TrySetResult(true);
                    await Advance.Task.WaitAsync(_closing.Token).ConfigureAwait(false);
                    await ChunkAsync(stream, "background progress ").ConfigureAwait(false);
                    Advanced.TrySetResult(true);
                    await Finish.Task.WaitAsync(_closing.Token).ConfigureAwait(false);
                    await ChunkAsync(stream, "finished").ConfigureAwait(false);
                }
                else
                {
                    await ChunkAsync(stream, "Streaming fixture").ConfigureAwait(false);
                }

                await WriteAsync(stream, "data: {\"id\":\"local\",\"object\":\"chat.completion.chunk\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":10,\"total_tokens\":20}}\n\ndata: [DONE]\n\n").ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
            catch (Exception error)
            {
                Volatile.Write(ref _failure, error.GetType().Name + ": " + error.Message);
            }
        }
    }

    /// <summary>Reads the provider client's declared HTTP body without depending on a second web server.</summary>
    /// <param name="stream">The request socket.</param><returns>The JSON request body.</returns>
    private async Task<string> RequestBodyAsync(NetworkStream stream)
    {
        using var received = new MemoryStream();
        var buffer = new byte[8192];
        var end = -1;
        var length = 0;
        while (received.Length < 2 * 1024 * 1024)
        {
            var count = await stream.ReadAsync(buffer, _closing.Token).ConfigureAwait(false);
            if (count == 0)
            {
                throw new IOException("The local provider request ended before its declared body.");
            }

            received.Write(buffer, 0, count);
            var raw = received.GetBuffer();
            if (end < 0)
            {
                end = Encoding.ASCII.GetString(raw, 0, (int)received.Length).IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (end >= 0)
                {
                    var headers = Encoding.ASCII.GetString(raw, 0, end).Split("\r\n", StringSplitOptions.None);
                    var header = headers.FirstOrDefault(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                    length = header is null ? 0 : int.Parse(header[(header.IndexOf(':') + 1)..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            if (end >= 0 && received.Length >= end + 4 + length)
            {
                return Encoding.UTF8.GetString(raw, end + 4, length);
            }
        }

        throw new IOException("The local provider fixture request exceeded its test bound.");
    }

    /// <summary>Writes one normal OpenAI-compatible content delta.</summary>
    /// <param name="stream">The provider response.</param><param name="text">The deterministic delta.</param><returns>The flush acknowledgement.</returns>
    private Task ChunkAsync(NetworkStream stream, string text) => WriteAsync(stream, "data: " + new JsonObject { ["id"] = "local", ["object"] = "chat.completion.chunk", ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["delta"] = new JsonObject { ["content"] = text }, ["finish_reason"] = null }) }.ToJsonString() + "\n\n");

    /// <summary>Flushes bytes so the kernel can observe each deliberate stream boundary.</summary>
    /// <param name="stream">The response socket.</param><param name="text">The HTTP or SSE bytes.</param><returns>The flush acknowledgement.</returns>
    private async Task WriteAsync(NetworkStream stream, string text)
    {
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text), _closing.Token).ConfigureAwait(false);
        await stream.FlushAsync(_closing.Token).ConfigureAwait(false);
    }
}
