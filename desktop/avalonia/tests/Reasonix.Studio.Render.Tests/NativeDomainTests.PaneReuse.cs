using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Events;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.Shell.Session;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>A provisional open cannot retire the visible blank runtime before its history is readable.</summary>
    /// <returns>The failed history read and retained original runtime.</returns>
    [Fact]
    public async Task FailedHistoryDoesNotCloseThePreviousRuntime()
    {
        using var reads = new ReferenceReads();
        using var http = new HttpClient(reads) { BaseAddress = new Uri("http://127.0.0.1:4000") };
        var closed = new List<string>();
        reads.Exchange = request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            HttpResponseMessage? response = null;
            if (request.Method == HttpMethod.Post && path == "/runtimes")
            {
                response = JsonReply(new JsonObject { ["id"] = "r2", ["base"] = "/rt/r2", ["root"] = "/next", ["sessionPath"] = "/next/failing.jsonl" });
            }
            else if (path.EndsWith("/close", StringComparison.Ordinal))
            {
                closed.Add(path);
                response = new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            else if (path == "/history/page")
            {
                response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            return Task.FromResult(response);
        };
        SessionEngine Engine(RuntimeDescriptor runtime) => new(new KernelClient(http), new SseEventTransport(http.BaseAddress.ToString(), http), descriptor => Engine(descriptor), runtime);
        var owner = Engine(new RuntimeDescriptor { Id = "r1", Base = "/rt/r1", Root = "/original" });
        await using var coordinator = new WindowSessionCoordinator(owner);
        var original = coordinator.Capture(owner, new SessionStatus { WorkspaceRoot = "/original" });
        coordinator.Focused = original;
        var next = await coordinator.OpenAsync(new SessionOpenTarget(string.Empty, "/next", "/next/failing.jsonl"), false, null, CancellationToken.None);
        await Assert.ThrowsAnyAsync<Exception>(() => next.Engine.LoadHistoryAsync());
        Assert.Empty(closed);
        Assert.Contains(original, coordinator.Panes);
        Assert.Same(original, coordinator.Focused);
    }

    /// <summary>Explicit limits reuse only settled local panes in the same project.</summary>
    /// <returns>The canonical Hub and resume requests.</returns>
    [Fact]
    public async Task PaneLimitsPreserveRunningWorkAndReuseAnIdleLocalProject()
    {
        using var reads = new ReferenceReads();
        using var http = new HttpClient(reads) { BaseAddress = new Uri("http://127.0.0.1:4000") };
        var resumes = new List<string>();
        var compositions = new List<string>();
        var opened = 0;
        reads.Exchange = async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/agent-composition")
            {
                compositions.Add((string?)JsonNode.Parse(await request.Content!.ReadAsStringAsync())?["id"] ?? string.Empty);
                return JsonReply(new JsonObject());
            }

            if (request.RequestUri!.AbsolutePath == "/resume")
            {
                resumes.Add((string?)JsonNode.Parse(await request.Content!.ReadAsStringAsync())?["path"] ?? string.Empty);
                return JsonReply(new JsonObject());
            }

            if (request.RequestUri.AbsolutePath == "/runtimes" && request.Method == HttpMethod.Post)
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
                var id = "r" + (++opened);
                return JsonReply(new JsonObject { ["id"] = id, ["base"] = "/rt/" + id, ["root"] = (string?)body["root"], ["sessionPath"] = (string?)body["sessionPath"] });
            }

            if (request.RequestUri.AbsolutePath == "/history/page")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.RequestUri.AbsolutePath == "/history")
            {
                return JsonReply(new JsonArray());
            }

            return null;
        };
        SessionEngine Engine(RuntimeDescriptor? runtime = null) => new(new KernelClient(http), new SseEventTransport(http.BaseAddress.ToString(), http), descriptor => Engine(descriptor), runtime);
        var owner = Engine();
        await using var coordinator = new WindowSessionCoordinator(owner) { Limit = 2 };
        var initial = coordinator.Capture(owner, new SessionStatus { WorkspaceRoot = "/w" });
        coordinator.Focused = initial;
        Assert.Same(initial, await coordinator.OpenAsync(new SessionOpenTarget(string.Empty, "/w", string.Empty), false, "user:test", CancellationToken.None));
        Assert.Equal(new[] { "user:test" }, compositions);
        var first = await coordinator.OpenAsync(new SessionOpenTarget(string.Empty, "/w", "/w/one.jsonl"), false, null, CancellationToken.None);
        Assert.Same(initial, first);
        Assert.Equal(0, opened);
        var other = await coordinator.OpenAsync(new SessionOpenTarget(string.Empty, "/other", "/other/one.jsonl"), false, null, CancellationToken.None);
        coordinator.Focused = other;
        var second = await coordinator.OpenAsync(new SessionOpenTarget(string.Empty, "/w", "/w/two.jsonl"), false, null, CancellationToken.None);
        Assert.Same(first, second);
        Assert.Equal(2, coordinator.Panes.Count);
        Assert.Equal(1, opened);
        Assert.Equal(new[] { "/w/one.jsonl", "/w/two.jsonl" }, resumes);
        second.Engine.RestoreRunning(true, second.Engine.Generation.Revision);
        var third = await coordinator.OpenAsync(new SessionOpenTarget(string.Empty, "/w", "/w/three.jsonl"), false, null, CancellationToken.None);
        Assert.NotSame(second, third);
        Assert.True(second.Engine.State.Running);
        Assert.Equal(3, coordinator.Panes.Count);
        Assert.Equal(2, resumes.Count);
    }
}
