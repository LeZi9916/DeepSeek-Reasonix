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

/// <summary>
/// Holds a refused request to the identity the kernel sent, rather than to a
/// message a caller would have to read.
/// </summary>
/// <remarks>
/// The kernel answers <c>/resume</c> with five different refusals, and each asks
/// the reader to do something else: a 409 is "wait for the turn", a 404 is "that
/// conversation is gone", a 413 is "too large to open", a 403 is "that path is
/// not in the conversations folder" and a 400 is "this request named something
/// that is not a transcript". A shell that received only "the request failed"
/// could not say which. These cases pin the status and the code at the client
/// boundary, where the answer is still whole.
/// </remarks>
public sealed class KernelRefusalTests
{
    /// <summary>
    /// Checks that the four statuses a switch can be refused with stay distinct,
    /// which is the whole point of carrying the refusal instead of a message.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task EachStatusIsDistinctToACaller()
    {
        foreach (var status in new[] { HttpStatusCode.Conflict, HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.NotFound, HttpStatusCode.BadRequest })
        {
            var refusal = await Refused(status, Coded("session.unknown"));

            Assert.Equal((int)status, refusal.Status);
        }
    }

    /// <summary>
    /// Checks that a coded body keeps its code and the values a sentence needs.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task ACodedAnswerKeepsItsCodeAndParams()
    {
        var refusal = await Refused(
            HttpStatusCode.RequestEntityTooLarge,
            "{\"code\":\"session.replay_limit\",\"error\":\"too large\",\"params\":{\"path\":\"/tmp/x.jsonl\"}}");

        Assert.Equal("session.replay_limit", refusal.Code);
        Assert.Equal("too large", refusal.Reason!.Error);
        Assert.NotNull(refusal.Reason.Params);
        Assert.Equal("/tmp/x.jsonl", refusal.Reason.Params!["path"].GetString());

        // A coded answer is one body; it is not also the plain-text account.
        Assert.Null(refusal.Detail);
    }

    /// <summary>
    /// Checks that a refusal with no code keeps the text the kernel wrote, because
    /// that text is the only account of that failure there is.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    /// <remarks>
    /// The kernel's own <c>http.Error</c> call sites — including the 409 a turn in
    /// flight answers <c>/resume</c> with — write a sentence and no envelope.
    /// Reading the body as JSON first would have thrown it away.
    /// </remarks>
    [Fact]
    public async Task APlainTextAnswerKeepsItsAccount()
    {
        var refusal = await Refused(
            HttpStatusCode.Conflict,
            "a turn is running on this conversation");

        Assert.Null(refusal.Code);
        Assert.Equal("a turn is running on this conversation", refusal.Detail);
        Assert.Equal("a turn is running on this conversation", refusal.Message);
    }

    /// <summary>
    /// Checks that an answer with no body still names the status, which is what a
    /// reader can act on when there is nothing else.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task AnEmptyAnswerStillNamesTheStatus()
    {
        var refusal = await Refused(HttpStatusCode.NotFound, string.Empty);

        Assert.Equal(404, refusal.Status);
        Assert.Null(refusal.Code);
        Assert.Null(refusal.Detail);
        Assert.Contains("404", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks that the refusal is what the client throws, not the framework's own
    /// status exception: a caller that catches the wrong type cannot branch.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task TheClientRefusesWithItsOwnType()
    {
        using var http = new HttpClient(new StubHandler(HttpStatusCode.NotFound, Coded("session.unknown")))
        {
            BaseAddress = new Uri("http://127.0.0.1:9"),
        };
        var client = new KernelClient(http);

        var error = await Assert.ThrowsAsync<KernelRefusal>(
            () => client.ResumeAsync("/home/someone/.reasonix/sessions/x.jsonl"));

        Assert.Equal(404, error.Status);
        Assert.Equal("session.unknown", error.Code);
    }

    /// <summary>
    /// Builds the envelope the kernel's <c>refuse</c> writes.
    /// </summary>
    /// <param name="code">The dotted code.</param>
    /// <returns>The JSON body.</returns>
    private static string Coded(string code)
    {
        return "{\"code\":\"" + code + "\",\"error\":\"refused\"}";
    }

    /// <summary>
    /// Answers one request with the given status and body, and reads the refusal
    /// the client produced from it.
    /// </summary>
    /// <param name="status">The status to answer with.</param>
    /// <param name="body">The body to answer with.</param>
    /// <returns>The refusal the client produced.</returns>
    private static async Task<KernelRefusal> Refused(HttpStatusCode status, string body)
    {
        using var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
            RequestMessage = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:9/resume"),
        };

        return await KernelRefusal.FromAsync(response, CancellationToken.None);
    }

    /// <summary>
    /// Answers every request with one prepared status and body.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        /// <summary>
        /// The status to answer with.
        /// </summary>
        private readonly HttpStatusCode _status;

        /// <summary>
        /// The body to answer with.
        /// </summary>
        private readonly string _body;

        /// <summary>
        /// Initializes a new instance of the <see cref="StubHandler"/> class.
        /// </summary>
        /// <param name="status">The status to answer with.</param>
        /// <param name="body">The body to answer with.</param>
        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
