using Reasonix.Studio.Contracts.Events;
using Reasonix.Studio.Contracts.Queue;
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
    /// Checks that a switch the kernel refused leaves the conversation that was
    /// already open on screen.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    /// <remarks>
    /// A 409 from <c>/resume</c> means a turn owns that session. Publishing a
    /// fresh state before knowing the switch landed would blank the rows the
    /// person is reading for a conversation that never opened.
    /// </remarks>
    [Fact]
    public async Task ARefusedSwitchKeepsTheConversation()
    {
        var refusal = new KernelRefusal("/resume", 409, null, "a turn is running on that conversation");
        var client = new FakeClient { Refuse = refusal };
        var engine = Engine(
            client,
            new FakeTransport("{\"kind\":\"text\",\"text\":\"still here\",\"seq\":1}"));

        await engine.StartAsync();

        var thrown = await Assert.ThrowsAsync<KernelRefusal>(() => engine.ResumeAsync("/sessions/other.jsonl"));

        Assert.Equal(409, thrown.Status);
        Assert.Equal("a turn is running on that conversation", thrown.Detail);
        Assert.Equal("still here", Assert.Single(engine.State.Items.OfType<SayItem>()).Text);
    }

    /// <summary>
    /// Checks that a session with no messages still publishes, so a listener can
    /// tell an empty conversation from one whose history has not arrived.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    /// <remarks>
    /// Before this, the empty read returned without a word. A window that had
    /// switched sessions kept the previous conversation's rows, because nothing
    /// told it the new one holds none.
    /// </remarks>
    [Fact]
    public async Task AnEmptyHistoryStillPublishes()
    {
        var engine = Engine(new FakeClient(), new FakeTransport());
        var published = new List<SessionState>();
        engine.Changed += state => published.Add(state);

        await engine.LoadHistoryAsync();

        var state = Assert.Single(published);
        Assert.Empty(state.Items);
        Assert.Empty(engine.State.Items);
        Assert.Equal(0, state.EnteredThrough);
    }

    /// <summary>
    /// Checks that an empty read replaces the rows a previous read left, which is
    /// what a switch into an empty conversation has to do.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task AnEmptyHistoryClearsTheRowsAReadLeftBehind()
    {
        var client = new FakeClient
        {
            Messages = new[]
            {
                new HistoryMessage { Role = "user", Content = "older conversation", MsgIndex = 0 },
            },
        };
        var engine = Engine(client, new FakeTransport());

        await engine.LoadHistoryAsync();
        Assert.Single(engine.State.Items);

        client.Messages = Array.Empty<HistoryMessage>();
        await engine.LoadHistoryAsync();

        Assert.Empty(engine.State.Items);
        Assert.Equal(0, engine.State.EnteredThrough);
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

        /// <summary>
        /// Gets or sets the refusal a switch answers with, or null when it lands.
        /// </summary>
        public Exception? Refuse
        {
            get; set;
        }

        /// <summary>
        /// Gets the lines this client was asked to queue, with the intent each carried.
        /// </summary>
        public List<SubmitRequest> Queued { get; } = new List<SubmitRequest>();

        /// <summary>
        /// Gets or sets the queue this client answers with.
        /// </summary>
        public QueueSnapshot Queue
        {
            get; set;
        } = new QueueSnapshot();

        /// <summary>
        /// Gets or sets the text one queued entry answers with.
        /// </summary>
        public string QueuedText
        {
            get; set;
        } = string.Empty;

        /// <summary>
        /// Gets the entries this client was asked to withdraw.
        /// </summary>
        public List<string> Withdrawn { get; } = new List<string>();

        /// <summary>
        /// Gets the entries this client was asked to move, with their new position.
        /// </summary>
        public List<(string Id, int To)> Moved { get; } = new List<(string, int)>();

        /// <summary>
        /// Gets the pause states this client was asked for, in order.
        /// </summary>
        public List<bool> Pauses { get; } = new List<bool>();

        /// <summary>
        /// Gets how many times the running turn was asked to stop.
        /// </summary>
        public int Cancelled
        {
            get; private set;
        }

        /// <summary>
        /// Gets or sets the tree this client answers with.
        /// </summary>
        public IReadOnlyList<Reasonix.Studio.Contracts.Tree.TreeWorkspace> Tree
        {
            get; set;
        } = Array.Empty<Reasonix.Studio.Contracts.Tree.TreeWorkspace>();

        /// <summary>
        /// Gets the window the last tree read asked for, or null for the default.
        /// </summary>
        public Reasonix.Studio.Contracts.Tree.TreeWindow? LastTreeWindow
        {
            get; private set;
        }

        /// <summary>
        /// Gets the pins this client was asked to set, with the value each carried.
        /// </summary>
        public List<(string Path, bool Pinned)> Pins { get; } = new List<(string, bool)>();

        /// <summary>
        /// Gets the archives this client was asked to set, with the value each carried.
        /// </summary>
        public List<(string Path, bool Archived)> ArchivedRows { get; } = new List<(string, bool)>();

        /// <summary>
        /// Gets the conversations this client was asked to mark seen.
        /// </summary>
        public List<string> Viewed { get; } = new List<string>();

        /// <summary>
        /// Gets how many times the window asked for a conversation with nothing in it.
        /// </summary>
        public int NewSessions
        {
            get; private set;
        }

        /// <summary>
        /// Gets the transcripts a tail copy was asked for.
        /// </summary>
        public List<string> Tails { get; } = new List<string>();

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
        public Task<IReadOnlyList<Reasonix.Studio.Contracts.Tree.TreeWorkspace>> TreeAsync(
            Reasonix.Studio.Contracts.Tree.TreeWindow? window = null,
            CancellationToken cancellationToken = default)
        {
            LastTreeWindow = window;
            return Task.FromResult(Tree);
        }

        /// <inheritdoc/>
        public Task PinSessionAsync(string path, bool pinned, CancellationToken cancellationToken = default)
        {
            Pins.Add((path, pinned));
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task ArchiveSessionAsync(string path, bool archived, CancellationToken cancellationToken = default)
        {
            ArchivedRows.Add((path, archived));
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task MarkSessionViewedAsync(string path, CancellationToken cancellationToken = default)
        {
            Viewed.Add(path);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task ResumeAsync(string path, CancellationToken cancellationToken = default)
        {
            Resumed.Add(path);
            return Refuse is null ? Task.CompletedTask : Task.FromException(Refuse);
        }

        /// <inheritdoc/>
        public Task NewSessionAsync(CancellationToken cancellationToken = default)
        {
            NewSessions++;
            return Refuse is null ? Task.CompletedTask : Task.FromException(Refuse);
        }

        /// <inheritdoc/>
        public Task<SessionTail> TailAsync(string path, CancellationToken cancellationToken = default)
        {
            Tails.Add(path);
            return Task.FromResult(new SessionTail { Path = path + "-tail", Dropped = 12 });
        }

        /// <inheritdoc/>
        public Task<ToolOutputPage> ToolOutputAsync(string callId, int offset = 0, int limit = 64 * 1024, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ToolOutputPage { CallId = callId });
        }

        /// <inheritdoc/>
        public Task<QueueSnapshot> QueueAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Queue);
        }

        /// <inheritdoc/>
        public Task<InboxReceipt> EnqueueAsync(SubmitRequest request, CancellationToken cancellationToken = default)
        {
            Queued.Add(request);
            return Refuse is null
                ? Task.FromResult(new InboxReceipt { ItemId = "item-" + Queued.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) })
                : Task.FromException<InboxReceipt>(Refuse);
        }

        /// <inheritdoc/>
        public Task<QueueEntry> ReadQueuedAsync(string id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new QueueEntry { Envelope = new PromptEnvelope { DisplayText = QueuedText } });
        }

        /// <inheritdoc/>
        public Task CancelQueuedAsync(string id, CancellationToken cancellationToken = default)
        {
            Withdrawn.Add(id);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task MoveQueuedAsync(string id, int toIndex, CancellationToken cancellationToken = default)
        {
            Moved.Add((id, toIndex));
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task RetryQueuedAsync(string id, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task SetQueuePausedAsync(bool paused, CancellationToken cancellationToken = default)
        {
            Pauses.Add(paused);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task CancelTurnAsync(CancellationToken cancellationToken = default)
        {
            Cancelled++;
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
