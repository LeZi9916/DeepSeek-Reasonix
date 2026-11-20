using Reasonix.Studio.Contracts.Events;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Events;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the vertical slice's seam: a frame that reached the subscription becomes
/// state a window can draw, and a reopened session opens on its transcript.
/// </summary>
/// <remarks>
/// The two sides are faked rather than socketed, because what needs proving is the
/// assembly — ordering, folding, and the restored transcript — not that a listener
/// answers. The pieces on either end are held by their own cases.
/// </remarks>
public sealed class SessionEngineTests
{
    /// <summary>
    /// Checks that a text frame reaching the subscription becomes a row with that
    /// text, which is the whole chain in one case.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task ATextFrameBecomesATranscriptRow()
    {
        var engine = Engine(
            client: new FakeClient(),
            transport: new FakeTransport("{\"kind\":\"text\",\"text\":\"hello\",\"seq\":1}"));

        await engine.StartAsync();

        var say = Assert.Single(engine.State.Items.OfType<SayItem>());
        Assert.Equal("hello", say.Text);
    }

    /// <summary>
    /// Checks that two frames arrive in order and both land, so a batch is not
    /// folded as one.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task FramesAreFoldedInTheOrderTheyArrived()
    {
        var engine = Engine(
            client: new FakeClient(),
            transport: new FakeTransport(
                "{\"kind\":\"text\",\"text\":\"first \",\"seq\":1}",
                "{\"kind\":\"text\",\"text\":\"second\",\"seq\":2}"));

        await engine.StartAsync();

        var say = Assert.Single(engine.State.Items.OfType<SayItem>());
        Assert.Equal("first second", say.Text);
    }

    /// <summary>
    /// Checks that a turn's end settles the message and records how it ended.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task ATurnEndSettlesTheMessage()
    {
        var engine = Engine(
            client: new FakeClient(),
            transport: new FakeTransport(
                "{\"kind\":\"text\",\"text\":\"done\",\"seq\":1}",
                "{\"kind\":\"turn_done\",\"seq\":2}"));

        await engine.StartAsync();

        Assert.All(engine.State.Items.OfType<SayItem>(), say => Assert.True(say.Done));
        Assert.False(engine.State.Running);
        Assert.IsType<CompletedTerminal>(engine.State.Terminal);
    }

    /// <summary>
    /// Checks that a reopened session opens on the messages the kernel already
    /// holds, rather than on an empty page.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task HistorySeedsTheTranscript()
    {
        var client = new FakeClient
        {
            Messages = new[]
            {
                new HistoryMessage { Role = "user", Content = "question", MsgIndex = 0, CreatedAt = 1_700_000_000_000 },
                new HistoryMessage { Role = "assistant", Content = "answer", MsgIndex = 1, Reasoning = "thinking" },
            },
        };
        var engine = Engine(client, new FakeTransport());

        await engine.LoadHistoryAsync();

        Assert.Equal(2, engine.State.Items.Count);
        Assert.Equal("question", Assert.IsType<UserItem>(engine.State.Items[0]).Text);
        var say = Assert.IsType<SayItem>(engine.State.Items[1]);
        Assert.Equal("answer", say.Text);
        Assert.Equal("thinking", say.Reasoning);

        // A restored transcript owes no entrance: those facts arrived, they are not
        // arriving.
        Assert.Equal(2, engine.State.EnteredThrough);
    }

    /// <summary>
    /// Checks that a line reaches the kernel as a submitted turn.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task ALineIsSubmittedAsATurn()
    {
        var client = new FakeClient();
        var engine = Engine(client, new FakeTransport());

        await engine.SubmitAsync("add retry to the http client");

        var sent = Assert.Single(client.Sent);
        Assert.Equal("add retry to the http client", sent.Input);
    }

    /// <summary>
    /// Checks that a hole the replay could not close is counted, because a view has
    /// to be able to say it is behind.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task AnUnclosableHoleIsCounted()
    {
        var engine = Engine(
            client: new FakeClient(),
            transport: new FakeTransport(
                "{\"kind\":\"text\",\"text\":\"a\",\"seq\":5}",
                "{\"kind\":\"stream_gap\"}"));

        await engine.StartAsync();

        Assert.Equal(1, engine.Gaps);
    }

    /// <summary>
    /// Builds an engine over the two fakes.
    /// </summary>
    /// <param name="client">The reads to use.</param>
    /// <param name="transport">The stream to use.</param>
    /// <returns>The engine, not yet started.</returns>
    private static SessionEngine Engine(IKernelClient client, IEventTransport transport)
    {
        return new SessionEngine(client, transport);
    }

    /// <summary>
    /// Answers the three reads without a socket.
    /// </summary>
    private sealed class FakeClient : IKernelClient
    {
        /// <summary>
        /// Gets or sets the messages a history read answers.
        /// </summary>
        public IReadOnlyList<HistoryMessage> Messages { get; set; } = Array.Empty<HistoryMessage>();

        /// <summary>
        /// Gets the lines that were submitted.
        /// </summary>
        public List<SubmitRequest> Sent { get; } = new List<SubmitRequest>();

        /// <summary>
        /// Gets the verdicts that were sent, as the id and the three scope flags.
        /// </summary>
        public List<(string Id, bool Allow, bool Session, bool Persist)> Verdicts
        {
            get;
        } =
            new List<(string, bool, bool, bool)>();

        /// <summary>
        /// Gets the transcripts that were asked for.
        /// </summary>
        public List<string> Resumed { get; } = new List<string>();

        /// <inheritdoc/>
        public Task<SessionStatus?> StatusAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<SessionStatus?>(new SessionStatus());
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<HistoryMessage>> HistoryAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Messages);
        }

        /// <inheritdoc/>
        public Task SubmitAsync(SubmitRequest request, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task ApproveAsync(string id, bool allow, bool session, bool persist, CancellationToken cancellationToken = default)
        {
            Verdicts.Add((id, allow, session, persist));
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<SessionEntry>> SessionsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<SessionEntry>>(Array.Empty<SessionEntry>());
        }

        /// <inheritdoc/>
        public Task ResumeAsync(string path, CancellationToken cancellationToken = default)
        {
            Resumed.Add(path);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Delivers a scripted sequence of frames and answers a replay that can close
    /// nothing.
    /// </summary>
    private sealed class FakeTransport : IEventTransport
    {
        /// <summary>
        /// The frames this transport will deliver, in order.
        /// </summary>
        private readonly string[] _frames;

        /// <summary>
        /// Initializes a new instance of the <see cref="FakeTransport"/> class.
        /// </summary>
        /// <param name="frames">The frames to deliver, as JSON text.</param>
        public FakeTransport(params string[] frames)
        {
            _frames = frames;
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<WireFrame> StreamAsync(
            Func<long?> cursor,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var json in _frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (WireFrame.TryParse(json, out var frame))
                {
                    yield return frame;
                }

                await Task.Yield();
            }
        }

        /// <inheritdoc/>
        public Task<ReplayResult> ReplayAsync(long after, CancellationToken cancellationToken)
        {
            // Nothing to close: the case that needs a hole wants it unclosable.
            return Task.FromResult(new ReplayResult(Array.Empty<WireFrame>(), false, after));
        }
    }
}
