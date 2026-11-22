using Reasonix.Studio.Kernel.Client;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>Checks the private side stream's completion and failure boundaries.</summary>
public sealed class ComposerTransportTests
{
    /// <summary>Preserves progress and requires the kernel's final answer frame.</summary>
    /// <param name="ending">Terminal stream frames.</param>
    /// <param name="code">Expected refusal or successful completion.</param>
    /// <returns>The decoded answer.</returns>
    [Theory]
    [InlineData("event: done\r\ndata: {\"answer\":\"final answer\"}\r\n\r\n", null)]
    [InlineData("event: error\ndata: {\"status\":409,\"code\":\"side.gone\",\"error\":\"closed\"}\n\n", "side.gone")]
    [InlineData("", "side.failed")]
    public async Task SideStreamRequiresCompletion(string ending, string? code)
    {
        using var handler = new SideHandler("event: progress\ndata: {\"kind\":\"text\",\"text\":\"partial\"}\n\n" + ending);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var output = new List<string>();
        var call = new StudioPageClient(http).AskSideAsync("side/id", "question", new CaptureProgress(output), default);
        if (code is null)
        {
            Assert.Equal("final answer", await call);
        }
        else
        {
            var refusal = await Assert.ThrowsAsync<KernelRefusal>(() => call);
            Assert.Equal(code, refusal.Code);
        }

        Assert.Equal("partial", Assert.Single(output));
    }

    /// <summary>Captures progress synchronously at the transport boundary.</summary>
    /// <param name="output">Observed fragments.</param>
    private sealed class CaptureProgress(List<string> output) : IProgress<string>
    {
        /// <inheritdoc/>
        public void Report(string value) => output.Add(value);
    }

    /// <summary>Supplies one isolated stream.</summary>
    /// <param name="frames">Server-sent events.</param>
    private sealed class SideHandler(string frames) : HttpMessageHandler
    {
        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/side/side%2Fid/ask", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(frames, Encoding.UTF8, "text/event-stream") });
        }
    }
}
