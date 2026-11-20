using Reasonix.Studio.Kernel.Boundary;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the shell's outbound half of the boundary: which requests carry the
/// credential, which must name their origin, and which must not leave at all.
/// </summary>
/// <remarks>
/// A request refused here is a defect the shell made; a request sent without the
/// credential is a defect that arrives as the kernel's 403, which reads like the
/// kernel's fault. Both are held by the same cases.
/// </remarks>
public sealed class CredentialHandlerTests
{
    /// <summary>
    /// The credential the kernel produces: 32 random bytes, hex-encoded.
    /// </summary>
    private const string Token = "9f2c1c0e5b7a4d3e8f6a1b2c3d4e5f60718293a4b5c6d7e8f9012345678abcde";

    /// <summary>
    /// The origin this launch answers for.
    /// </summary>
    private static readonly Uri s_origin = new Uri("http://127.0.0.1:7421/");

    /// <summary>
    /// Checks that a write carries both the credential and the exact origin.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task AWriteCarriesTheCredentialAndTheOrigin()
    {
        var recorder = new Recorder();
        using var client = Client(recorder);

        using var response = await client.PostAsync(new Uri(s_origin, "/submit"), new StringContent("{}"));

        var sent = Assert.Single(recorder.Requests);
        Assert.Equal("reasonix_token=" + Token, sent.Cookie);
        Assert.Equal("http://127.0.0.1:7421", sent.Origin);
    }

    /// <summary>
    /// Checks that a read carries the credential but needs no origin, because a
    /// top-level navigation sends none and the gate allows that for GET.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task AReadCarriesTheCredentialAndNoOrigin()
    {
        var recorder = new Recorder();
        using var client = Client(recorder);

        using var response = await client.GetAsync(new Uri(s_origin, "/history"));

        var sent = Assert.Single(recorder.Requests);
        Assert.Equal("reasonix_token=" + Token, sent.Cookie);
        Assert.Null(sent.Origin);
    }

    /// <summary>
    /// Checks that a request to another origin never leaves, because sending it
    /// would hand this launch's credential to whatever listens on that port.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task ARequestToAnotherOriginIsRefusedBeforeItIsSent()
    {
        var recorder = new Recorder();
        using var client = Client(recorder);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAsync(new Uri("http://127.0.0.1:7422/elsewhere")));

        Assert.Contains("refusing to send it to", refused.Message, StringComparison.Ordinal);
        Assert.Empty(recorder.Requests);
    }

    /// <summary>
    /// Checks that a wildcard address is a different origin from the loopback one:
    /// one more request that would have reached a different listener.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task AWildcardAddressIsNotThisListener()
    {
        var recorder = new Recorder();
        using var client = Client(recorder);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAsync(new Uri("http://0.0.0.0:7421/")));

        Assert.Empty(recorder.Requests);
    }

    /// <summary>
    /// Checks that two addresses on one listener are one origin and a different port
    /// is not, which is the whole comparison the refusal above rests on.
    /// </summary>
    [Fact]
    public void OriginComparisonIsSchemeHostAndPort()
    {
        Assert.True(CredentialHandler.SameOrigin(new Uri("http://127.0.0.1:7421/a"), s_origin));
        Assert.True(CredentialHandler.SameOrigin(new Uri("http://127.0.0.1:7421/b?x=1"), s_origin));

        Assert.False(CredentialHandler.SameOrigin(new Uri("http://127.0.0.1:7422/"), s_origin));
        Assert.False(CredentialHandler.SameOrigin(new Uri("https://127.0.0.1:7421/"), s_origin));
        Assert.False(CredentialHandler.SameOrigin(new Uri("http://localhost:7421/"), s_origin));
    }

    /// <summary>
    /// Checks which methods are treated as changing state, because the origin rule
    /// is split on exactly that.
    /// </summary>
    [Fact]
    public void OnlyReadsAreExemptFromNamingTheirOrigin()
    {
        Assert.False(CredentialHandler.ChangesState(HttpMethod.Get));
        Assert.False(CredentialHandler.ChangesState(HttpMethod.Head));

        Assert.True(CredentialHandler.ChangesState(HttpMethod.Post));
        Assert.True(CredentialHandler.ChangesState(HttpMethod.Put));
        Assert.True(CredentialHandler.ChangesState(HttpMethod.Patch));
        Assert.True(CredentialHandler.ChangesState(HttpMethod.Delete));
    }

    /// <summary>
    /// Builds a client that sends through a recorder instead of a socket.
    /// </summary>
    /// <param name="recorder">The handler that records what was sent.</param>
    /// <returns>The client the cases use.</returns>
    private static HttpClient Client(Recorder recorder)
    {
        var cookie = new LaunchCookie(s_origin, Token);
        var handler = new CredentialHandler(cookie, recorder);
        return new HttpClient(handler) { BaseAddress = s_origin };
    }

    /// <summary>
    /// Stands in for the kernel's listener, recording what each request carries
    /// instead of answering it over a socket.
    /// </summary>
    private sealed class Recorder : HttpMessageHandler
    {
        /// <summary>
        /// Gets what each request carried, in the order they arrived.
        /// </summary>
        public System.Collections.Generic.List<Sent> Requests { get; } = new System.Collections.Generic.List<Sent>();

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var cookie = request.Headers.TryGetValues("Cookie", out var cookies)
                ? string.Join("; ", cookies)
                : null;
            var origin = request.Headers.TryGetValues("Origin", out var origins)
                ? string.Join("; ", origins)
                : null;

            Requests.Add(new Sent(cookie, origin));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    /// <summary>
    /// What one request carried at the boundary headers.
    /// </summary>
    /// <param name="Cookie">The cookie header, or null when none was sent.</param>
    /// <param name="Origin">The origin header, or null when none was sent.</param>
    private sealed record Sent(string? Cookie, string? Origin);
}
