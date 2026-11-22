using Reasonix.Studio.Kernel.Events;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>Ensures an ended event stream yields before reconnecting.</summary>
public sealed class SseReconnectTests
{
    /// <summary>A synchronously closed connection cannot spin or monopolize the caller.</summary>
    /// <returns>The bounded reconnect attempt.</returns>
    [Fact]
    public async Task AnEmptyStreamBacksOffBeforeReconnecting()
    {
        using var handler = new EmptyStreams();
        using var http = new HttpClient(handler);
        var transport = new SseEventTransport("http://127.0.0.1:4000", http);
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Task.Run(async () =>
        {
            try
            {
                await foreach (var _ in transport.StreamAsync(() => null, stop.Token))
                {
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        });
        Assert.InRange(handler.Requests, 1, 2);
    }

    /// <summary>Immediately ends every successful SSE response.</summary>
    private sealed class EmptyStreams : HttpMessageHandler
    {
        /// <summary>Counts attempted SSE connections.</summary>
        private int _requests;

        /// <summary>Gets the connection attempt count.</summary>
        internal int Requests => Volatile.Read(ref _requests);

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) });
        }
    }
}
