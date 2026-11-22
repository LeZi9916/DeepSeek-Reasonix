using Reasonix.Studio.Kernel.Client;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>Checks page actions at the HTTP boundary shared with Electron.</summary>
public sealed class StudioPageClientTests
{
    /// <summary>Saves against the version opened by the editor and preserves a typed conflict.</summary>
    /// <returns>The request and response check.</returns>
    [Fact]
    public async Task SaveCarriesTheOriginalRevision()
    {
        using var handler = new Handler(async request =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("/workspace/file", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("a.md", body.RootElement.GetProperty("path").GetString());
            Assert.Equal("new text", body.RootElement.GetProperty("content").GetString());
            Assert.Equal("original", body.RootElement.GetProperty("revision").GetString());
            return new HttpResponseMessage(HttpStatusCode.Conflict) { RequestMessage = request };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:1234/") };
        var pages = new StudioPageClient(http);
        var opened = new WorkspaceFile("a.md", "old text", "original");
        var refusal = await Assert.ThrowsAsync<KernelRefusal>(() => pages.SaveFileAsync(opened with { Content = "new text" }, default));
        Assert.Equal(409, refusal.Status);
    }

    /// <summary>Isolation sends the chosen worktree source, ref and carried paths.</summary>
    /// <returns>The workspace switch request.</returns>
    [Fact]
    public async Task IsolateWorkspaceSendsTheWorktreeSelection()
    {
        using var handler = new Handler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/workspace", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.True(body.RootElement.GetProperty("isolate").GetBoolean());
            var worktree = body.RootElement.GetProperty("worktree");
            Assert.Equal("refs/heads/topic", worktree.GetProperty("ref").GetString());
            Assert.Equal("/repo", worktree.GetProperty("sourceRoot").GetString());
            Assert.Equal("/repo/notes.md", worktree.GetProperty("attachments")[0].GetString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"workspaceRoot":"/worktrees/topic"}""", Encoding.UTF8, "application/json"),
            };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:1234/") };
        var root = await new StudioPageClient(http).IsolateWorkspaceAsync("/repo", "refs/heads/topic", ["/repo/notes.md"], default);
        Assert.Equal("/worktrees/topic", root);
    }

    /// <summary>Long paste keeping sends text kind and reads the saved pointer.</summary>
    /// <returns>The saved pointer.</returns>
    [Fact]
    public async Task KeepingTextCarriesTheTextKind()
    {
        using var handler = new Handler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/attachments", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("text", body.RootElement.GetProperty("kind").GetString());
            Assert.Equal("held text", body.RootElement.GetProperty("text").GetString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"path":"workspace/.reasonix/paste.txt","pointer":"[kept paste]"}""", Encoding.UTF8, "application/json"),
            };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:1234/") };
        var kept = await new StudioPageClient(http).KeepTextAsync("held text", default);
        Assert.Equal("workspace/.reasonix/paste.txt", kept.Path);
        Assert.Equal("[kept paste]", kept.Pointer);
    }

    /// <summary>Uses UTF-16 caret positions and the requesting window's language.</summary>
    /// <returns>The completion read.</returns>
    [Fact]
    public async Task CompletionUsesTheKernelsReplacementSpan()
    {
        using var handler = new Handler(request =>
        {
            Assert.Contains("cursor=5", request.RequestUri!.Query, StringComparison.Ordinal);
            Assert.Contains("lang=zh", request.RequestUri.Query, StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"from":3,"to":5,"items":[{"label":"文件","insert":"@文件 " }]}""", Encoding.UTF8, "application/json"),
            });
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:1234/") };
        var result = await new StudioPageClient(http).CompleteAsync("😀 @文", 5, "zh", default);
        Assert.Equal(3, result.From);
        Assert.Equal("@文件 ", Assert.Single(result.Items).Insert);
    }

    /// <summary>Changing a desktop notification preserves the phone rules and all other switches.</summary>
    /// <returns>The observed read-before-write.</returns>
    [Fact]
    public async Task NotificationPatchRetainsThePhoneConfiguration()
    {
        var calls = 0;
        using var handler = new Handler(async request =>
        {
            calls++;
            Assert.Equal("/notifications", request.RequestUri!.AbsolutePath);
            const string Current = """{"enabled":true,"sound":false,"turnDone":true,"phone":{"approval":true,"input":false}}""";
            if (calls == 1)
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Current, Encoding.UTF8, "application/json") };
            }

            Assert.Equal(HttpMethod.Put, request.Method);
            var saved = await request.Content!.ReadAsStringAsync();
            using var parsed = JsonDocument.Parse(saved);
            Assert.True(parsed.RootElement.GetProperty("sound").GetBoolean());
            Assert.True(parsed.RootElement.GetProperty("turnDone").GetBoolean());
            Assert.True(parsed.RootElement.GetProperty("phone").GetProperty("approval").GetBoolean());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(saved, Encoding.UTF8, "application/json") };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:1234/") };
        var result = await new StudioPageClient(http).SetNotificationAsync("sound", true, default);
        Assert.Equal(2, calls);
        Assert.True((bool?)result?["sound"]);
    }

    /// <summary>Supplies an observed HTTP response.</summary>
    /// <param name="respond">The check on each request.</param>
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return respond(request);
        }
    }
}
