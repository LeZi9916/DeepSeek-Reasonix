using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Session;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>Hub-scoped runtime operations never acquire the focused pane's prefix.</summary>
public sealed class RuntimeRoutingTests
{
    /// <summary>A background pane is paused at its own canonical route.</summary>
    /// <returns>The recorded authenticated routing boundary.</returns>
    [Fact]
    public async Task PausingAnotherRuntimeKeepsItsHubPath()
    {
        using var sink = new RoutingSink();
        var type = typeof(SessionEngine).Assembly.GetType("Reasonix.Studio.Kernel.Session.ProjectRuntimeHandler")!;
        using var routing = (HttpMessageHandler)Activator.CreateInstance(type, "/rt/r7", sink)!;
        using var http = new HttpClient(routing) { BaseAddress = new Uri("http://127.0.0.1:44000") };
        var pages = new StudioPageClient(http);
        await pages.WorkspaceRuntimeActionAsync("r9", false, default);
        Assert.Equal("/rt/r9/cancel", sink.Path);
        using var status = await http.GetAsync("/status");
        Assert.Equal("/rt/r7/status", sink.Path);
    }

    /// <summary>Records the final request path after runtime routing.</summary>
    private sealed class RoutingSink : HttpMessageHandler
    {
        /// <summary>Gets the most recent path at the transport boundary.</summary>
        internal string Path { get; private set; } = string.Empty;

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }
}
