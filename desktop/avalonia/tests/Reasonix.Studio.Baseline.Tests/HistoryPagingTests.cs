using Reasonix.Studio.Contracts.Events;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Events;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>Verifies paged transcript restoration against the HTTP contract.</summary>
public sealed class HistoryPagingTests
{
    /// <summary>Earlier pages cannot erase a turn that finished after the snapshot was read.</summary>
    /// <returns>The paged history and independently arriving turn.</returns>
    [Fact]
    public async Task PrependingKeepsCompletedLiveRowsOutsideTheHistorySnapshot()
    {
        using var handler = new HistoryHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:4000") };
        await using var engine = new SessionEngine(new KernelClient(http), new HistoryLiveTransport());
        await engine.LoadHistoryAsync();
        await engine.StartAsync();
        Assert.Contains(engine.State.Items, row => row is SayItem { Text: "live answer", Done: true });
        await engine.LoadEarlierHistoryAsync();
        Assert.Contains(engine.State.Items, row => row is SayItem { Text: "live answer", Done: true });
        Assert.Single(engine.State.Items.OfType<ToolItem>());
    }

    /// <summary>Joins a result to its dispatch across page boundaries.</summary>
    /// <returns>The restored history.</returns>
    [Fact]
    public async Task APageRestoresToolContextAndPrependsWithoutDuplicates()
    {
        using var handler = new HistoryHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:4000") };
        await using var engine = new SessionEngine(new KernelClient(http), new SseEventTransport(http.BaseAddress.ToString(), http));
        await engine.LoadHistoryAsync();
        Assert.True(engine.History.HasMore);
        Assert.Equal(2, engine.History.Earlier);
        var call = Assert.Single(engine.State.Items.OfType<ToolItem>());
        Assert.Equal("call-1", call.Call.ID);
        Assert.Equal("actual file contents", call.Call.Output);
        await engine.LoadEarlierHistoryAsync();
        Assert.False(engine.History.HasMore);
        Assert.Single(engine.State.Items.OfType<ToolItem>());
        Assert.Equal("question", Assert.IsType<UserItem>(engine.State.Items[0]).Text);
        Assert.Equal("final reply", Assert.IsType<SayItem>(engine.State.Items[^1]).Text);
        Assert.Equal(0, handler.LegacyReads);
    }

    /// <summary>Falls back only for an absent capability and retains history on failure.</summary>
    /// <returns>The unsupported and failed reads.</returns>
    [Fact]
    public async Task UnsupportedPaginationFallsBackButAReadFailureDoesNotEraseHistory()
    {
        using var handler = new HistoryHandler { Legacy = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:4000") };
        await using var engine = new SessionEngine(new KernelClient(http), new SseEventTransport(http.BaseAddress.ToString(), http));
        await engine.LoadHistoryAsync();
        Assert.Single(engine.State.Items);
        Assert.Equal(1, handler.LegacyReads);
        handler.Fail = true;
        await Assert.ThrowsAsync<KernelRefusal>(() => engine.LoadHistoryAsync());
        Assert.Single(engine.State.Items);
        Assert.Equal(1, handler.LegacyReads);
    }

    /// <summary>Delivers a live turn after the retained history snapshot.</summary>
    private sealed class HistoryLiveTransport : IEventTransport
    {
        /// <inheritdoc/>
        public async IAsyncEnumerable<WireFrame> StreamAsync(Func<long?> cursor, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var json in new[] { "{\"kind\":\"turn_started\",\"msgIndex\":4,\"seq\":1}", "{\"kind\":\"text\",\"text\":\"live answer\",\"seq\":2}", "{\"kind\":\"turn_done\",\"seq\":3}" })
            {
                Assert.True(WireFrame.TryParse(json, out var frame));
                yield return frame;
                await Task.Yield();
            }
        }

        /// <inheritdoc/>
        public Task<ReplayResult> ReplayAsync(long after, CancellationToken cancellationToken) => Task.FromResult(new ReplayResult(Array.Empty<WireFrame>(), false, after));
    }

    /// <summary>Serves canonical immutable history windows and typed refusals.</summary>
    private sealed class HistoryHandler : HttpMessageHandler
    {
        /// <summary>Gets or initializes a value indicating whether the host lacks pagination.</summary>
        internal bool Legacy
        {
            get; init;
        }

        /// <summary>Gets or sets a value indicating whether reads return a typed failure.</summary>
        internal bool Fail
        {
            get; set;
        }

        /// <summary>Gets the number of legacy-history requests.</summary>
        internal int LegacyReads
        {
            get; private set;
        }

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                return Task.FromResult(Reply("{\"code\":\"history.unavailable\",\"error\":\"read failed\"}", HttpStatusCode.InternalServerError));
            }

            if (request.RequestUri!.AbsolutePath == "/history")
            {
                LegacyReads++;
                return Task.FromResult(Reply("[{\"role\":\"user\",\"content\":\"legacy question\",\"msgIndex\":0}]"));
            }

            Assert.Equal("/history/page", request.RequestUri.AbsolutePath);
            if (Legacy)
            {
                return Task.FromResult(Reply("{\"code\":\"history.pagination_unsupported\",\"error\":\"not supported\"}", HttpStatusCode.NotFound));
            }

            var first = request.RequestUri.Query.Length == 0;
            return Task.FromResult(Reply(first
                ? "{\"messages\":[{\"role\":\"tool\",\"toolCallId\":\"call-1\",\"content\":\"actual file contents\",\"msgIndex\":2},{\"role\":\"assistant\",\"content\":\"final reply\",\"msgIndex\":3}],\"toolContext\":[{\"messageIndex\":1,\"ordinal\":0,\"call\":{\"id\":\"call-1\",\"name\":\"read_file\",\"arguments\":\"{\\\"path\\\":\\\"a.md\\\"}\"}}],\"snapshot\":\"s1\",\"start\":2,\"end\":4,\"total\":4,\"hasMoreBefore\":true,\"nextCursor\":\"previous\"}"
                : "{\"messages\":[{\"role\":\"user\",\"content\":\"question\",\"msgIndex\":0},{\"role\":\"assistant\",\"content\":\"\",\"toolCalls\":[{\"id\":\"call-1\",\"name\":\"read_file\",\"arguments\":\"{\\\"path\\\":\\\"a.md\\\"}\"}],\"msgIndex\":1}],\"toolContext\":[],\"snapshot\":\"s1\",\"start\":0,\"end\":2,\"total\":4,\"hasMoreBefore\":false}"));
        }

        /// <summary>Creates a canonical JSON history response.</summary>
        /// <param name="json">Response body.</param><param name="code">HTTP status.</param><returns>The response.</returns>
        private static HttpResponseMessage Reply(string json, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
