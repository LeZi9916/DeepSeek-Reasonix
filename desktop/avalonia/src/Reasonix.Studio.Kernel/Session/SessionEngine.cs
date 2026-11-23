using Reasonix.Studio.Contracts.Events;
using Reasonix.Studio.Contracts.Queue;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Events;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Session;

/// <summary>
/// One conversation, end to end: the kernel it reads, the stream it folds, and the
/// state a window draws.
/// </summary>
/// <remarks>
/// This is the seam the vertical slice exists to prove. A frame arrives on the
/// socket, the subscription decides whether it may be folded yet, the reducer turns
/// it into state, and the window is told once per fold — the same path the browser
/// client takes, with the same ordering rules, so a resumed window and a live one
/// agree.
///
/// The reads and the stream are taken as interfaces so this assembly can be held by
/// a test without a socket. What needs proving is that a frame which reached the
/// subscription becomes state, and that a reopened session opens on its transcript;
/// neither of those depends on a listener existing.
/// </remarks>
public sealed partial class SessionEngine : IAsyncDisposable
{
    /// <summary>
    /// How many bytes one page of a call's output carries.
    /// </summary>
    private const int FullOutputPageBytes = 64 * 1024;

    /// <summary>
    /// How many pages a card will read back before the rest stays in the kernel.
    /// </summary>
    private const int FullOutputPages = 64;

    /// <summary>
    /// Reads status and history, and submits lines.
    /// </summary>
    private readonly IKernelClient _client;

    /// <summary>
    /// Carries the live stream and its replay.
    /// </summary>
    private readonly IEventTransport _transport;

    /// <summary>
    /// Turns frames into state, exactly once each and in order.
    /// </summary>
    private readonly EventSubscription _subscription;

    /// <summary>
    /// Folds one frame into the state.
    /// </summary>
    private readonly SessionReducer _reducer = new SessionReducer(new IdSequence());

    /// <summary>
    /// Cancels the stream when the conversation closes.
    /// </summary>
    private readonly CancellationTokenSource _closing = new CancellationTokenSource();

    /// <summary>
    /// The state as it stands, guarded because the stream writes it from its own loop.
    /// </summary>
    private readonly object _sync = new object();

    /// <summary>
    /// Releases what this conversation opened beyond its reads, or null when the
    /// caller supplied them and owns them.
    /// </summary>
    private readonly Func<ValueTask>? _release;

    /// <summary>
    /// The kernel's authority, which is the machine half of a draft's key.
    /// </summary>
    private readonly string _host;

    /// <summary>Provides new authenticated connections for another runtime in this same launch.</summary>
    private readonly LaunchedKernel? _kernel;

    /// <summary>Constructs a runtime over explicitly supplied authenticated transports.</summary>
    private readonly Func<RuntimeDescriptor, SessionEngine>? _runtimeFactory;

    /// <summary>
    /// The tree's own stream, or null when the caller supplied the reads.
    /// </summary>
    /// <remarks>
    /// The tree changes on the hub, outside any one conversation: a pin, an
    /// archive, a save from another window. Its stream carries those notices and
    /// nothing else, so it is read separately from the conversation's.
    /// </remarks>
    private readonly SseEventTransport? _tree;

    /// <summary>The tree stream retained independently of the conversation budget.</summary>
    private Task? _treeWatching;

    /// <summary>
    /// The state as it stands.
    /// </summary>
    private SessionState _state = new SessionState();

    /// <summary>
    /// How many holes the stream could not close.
    /// </summary>
    private int _gaps;

    /// <summary>
    /// Whether this conversation has been released, so a second release is a no-op.
    /// </summary>
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionEngine"/> class over one
    /// launch's kernel.
    /// </summary>
    /// <param name="kernel">The kernel this conversation reads.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="kernel"/> is null.</exception>
    public SessionEngine(LaunchedKernel kernel)
        : this(kernel, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionEngine"/> class over
    /// reads the caller supplied and therefore owns.
    /// </summary>
    /// <param name="client">Reads status and history, and submits lines.</param>
    /// <param name="transport">Carries the live stream and its replay.</param>
    /// <param name="runtimeFactory">Constructs scoped engines for caller-owned transports.</param><param name="runtime">Optional validated runtime identity.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="client"/> or <paramref name="transport"/> is null.
    /// </exception>
    public SessionEngine(IKernelClient client, IEventTransport transport, Func<RuntimeDescriptor, SessionEngine>? runtimeFactory = null, RuntimeDescriptor? runtime = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _host = string.Empty;
        _tree = null;
        _subscription = new EventSubscription(transport, Fold, ReportGap);
        _release = null;
        _runtimeFactory = runtimeFactory;
        Runtime = runtime;
    }

    /// <summary>Initializes a new instance of the <see cref="SessionEngine"/> class on a specific runtime.</summary>
    /// <param name="kernel">The same local host process.</param>
    /// <param name="runtime">The validated pane base. The default engine owns the process.</param>
    /// <param name="remote">Whether the hub hosts the pane on another machine.</param>
    private SessionEngine(LaunchedKernel kernel, string? runtime, bool remote = false)
    {
        if (kernel is null)
        {
            throw new ArgumentNullException(nameof(kernel));
        }

        // One handler for both reads and the stream, so a connection is pooled
        // rather than rebuilt and the credential rides exactly one place: a second
        // handler would be a second place to keep correct.
        _kernel = kernel;
        IsRemote = remote;
        HttpMessageHandler handler = new Boundary.CredentialHandler(kernel.Cookie());
        if (runtime is not null)
        {
            handler = new ProjectRuntimeHandler(runtime, handler);
        }

        var http = new HttpClient(handler)
        {
            BaseAddress = kernel.Origin,
            Timeout = Timeout.InfiniteTimeSpan,
        };

        _client = new KernelClient(http);
        var transport = new SseEventTransport(kernel.Origin.ToString(), http);
        _transport = transport;
        _host = kernel.Origin.GetLeftPart(UriPartial.Authority);
        _tree = new SseEventTransport(kernel.Origin.ToString(), "/tree/events", "/tree/events/replay", http);
        _subscription = new EventSubscription(transport, Fold, ReportGap);
        _release = () =>
        {
            // The transports close the client's connections; the kernel then stops.
            transport.Dispose();
            _tree.Dispose();
            if (runtime is null)
            {
                kernel.Dispose();
            }

            return ValueTask.CompletedTask;
        };
    }

    /// <summary>
    /// Raised once per fold, with the state the fold produced.
    /// </summary>
    /// <remarks>
    /// Raised on the stream's own loop rather than marshalled here: a view decides
    /// where it may draw, and doing it here would decide that for every view.
    /// </remarks>
    public event Action<SessionState>? Changed;

    /// <summary>Raised for accepted ordered frames, including their live or replay provenance.</summary>
    public event Action<SessionEngine, WireFrame>? FrameReceived;

    /// <summary>Raised when the host log cannot repair a missing event range.</summary>
    public event Action<SessionEngine>? HistoryInvalidated;

    /// <summary>
    /// Raised when the kernel says its queue moved.
    /// </summary>
    /// <remarks>
    /// The frame carries no content — one authority answers every client — so a
    /// listener re-reads the queue rather than patching what it had. Raised on
    /// the stream's loop for the same reason <see cref="Changed"/> is.
    /// </remarks>
    public event Action? QueueChanged;

    /// <summary>
    /// Raised when the kernel says the sidebar's tree moved.
    /// </summary>
    /// <remarks>
    /// A pin, an archive, a rename or a save from another window reaches this
    /// window as this notice. It carries no content — the tree is read back — so a
    /// listener re-reads it rather than patching what it had.
    /// </remarks>
    public event Action? TreeChanged;

    /// <summary>Gets the hub identity when this engine addresses a named runtime.</summary>
    public RuntimeDescriptor? Runtime
    {
        get; private set;
    }

    /// <summary>Gets a value indicating whether this connection can construct hub-scoped conversations.</summary>
    public bool CanOpenProjects => _kernel is not null || _runtimeFactory is not null;

    /// <summary>Gets live generation observations and host-recorded round measurements.</summary>
    public GenerationTelemetry Generation { get; private set; } = new();

    /// <summary>
    /// Gets the authority of the kernel this conversation reads, or empty when
    /// the caller supplied its own reads.
    /// </summary>
    /// <remarks>
    /// It is the machine half of a draft's key: two windows on two kernels keep
    /// their unsent lines apart even when both opened the same workspace.
    /// </remarks>
    public string Host => _host;

    /// <summary>Gets a value indicating whether this runtime is a remote pane.</summary>
    public bool IsRemote
    {
        get;
    }

    /// <summary>Gets the authenticated endpoints backing native pages.</summary>
    public StudioPageClient Pages => _client.Pages;

    /// <summary>
    /// Gets the state as it stands.
    /// </summary>
    public SessionState State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// Gets the number of holes the stream could not close, for a diagnostic line.
    /// </summary>
    public int Gaps => Volatile.Read(ref _gaps);

    /// <summary>Sends the answers and retains them on the same prompt when the kernel accepts them.</summary>
    /// <param name="id">Kernel ask id.</param>
    /// <param name="answers">Selections in question order.</param>
    /// <param name="token">Cancels the write.</param>
    /// <returns>The kernel acknowledgement.</returns>
    public async Task AnswerAsync(string id, IReadOnlyList<QuestionSelection> answers, CancellationToken token)
    {
        await Pages.AnswerAsync(id, answers, token).ConfigureAwait(false);
        SessionState next;
        lock (_sync)
        {
            next = PromptDecisions.Answer(_state, id, answers.Select(answer => answer.Selected).ToArray());
            _state = next;
        }

        Changed?.Invoke(next);
    }

    /// <summary>Requests cancellation of the active turn independently of the queue.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The kernel acknowledgement.</returns>
    public Task CancelTurnAsync(CancellationToken cancellationToken = default)
    {
        return _client.CancelTurnAsync(cancellationToken);
    }

    /// <summary>Registers a chosen folder and constructs the engine for its returned runtime.</summary>
    /// <param name="path">The local project folder.</param>
    /// <param name="token">Cancels the opening.</param>
    /// <returns>An independently disposable engine on the same launch.</returns>
    /// <param name="register">Whether to add a new folder to the project tree.</param>
    /// <param name="session">Existing transcript, whose saved composition takes precedence.</param>
    /// <param name="preset">Device-selected composition for a new session.</param>
    public async Task<SessionEngine> OpenProjectAsync(string path, CancellationToken token, bool register = true, string? session = null, string? preset = null)
    {
        var descriptor = await Pages.OpenProjectRuntimeAsync(path, register, session, preset, token).ConfigureAwait(false);
        return CreateRuntime(descriptor);
    }

    /// <summary>Stops watching a replaced pane while its owning host remains available.</summary>
    /// <returns>Cancellation of the old stream.</returns>
    public Task StopWatchingAsync() => _closing.CancelAsync();

    /// <summary>
    /// Reads what the kernel says about the session before anything is folded.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The status, or null when the kernel answered without one.</returns>
    public Task<SessionStatus?> StatusAsync(CancellationToken cancellationToken = default)
    {
        return _client.StatusAsync(cancellationToken);
    }

    /// <summary>
    /// Attaches the live stream and keeps it open until the conversation closes.
    /// </summary>
    /// <returns>A task that completes when the stream ends.</returns>
    /// <remarks>
    /// The tree's stream runs beside the conversation's: the two are separate
    /// endpoints, and a window that stopped reading the tree when the conversation
    /// went quiet would miss a pin made in another window.
    /// </remarks>
    public async Task StartAsync()
    {
        var tree = EnsureTreeWatching();
        await WatchConversationAsync().ConfigureAwait(false);
        await tree.ConfigureAwait(false);
    }

    /// <summary>Starts the tree stream once and retains it while session streams are reassigned.</summary>
    /// <returns>The tree lifetime.</returns>
    public Task EnsureTreeWatching()
    {
        lock (_sync)
        {
            return _treeWatching ??= _tree is null ? Task.CompletedTask : WatchTreeAsync(_tree);
        }
    }

    /// <summary>Reads live conversation events until the pane gives up its stream.</summary>
    /// <param name="token">The pane's current subscription lifetime.</param>
    /// <returns>The live event read.</returns>
    public async Task WatchConversationAsync(CancellationToken token = default)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token, token);
        try
        {
            await foreach (var frame in _transport.StreamAsync(() => _subscription.Position, lifetime.Token).ConfigureAwait(false))
            {
                await _subscription.AcceptAsync(frame, lifetime.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// Sends one typed line as a turn.
    /// </summary>
    /// <param name="line">The line the person typed.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>A task that completes once the kernel accepted the line.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="line"/> is null.</exception>
    public Task SubmitAsync(string line, CancellationToken cancellationToken = default)
    {
        if (line is null)
        {
            throw new ArgumentNullException(nameof(line));
        }

        return _client.SubmitAsync(new SubmitRequest(line, line), cancellationToken);
    }

    /// <summary>
    /// Sends one typed line, telling the kernel what it becomes.
    /// </summary>
    /// <param name="line">The line the person typed.</param>
    /// <param name="steer">Whether the line is guidance for the running turn.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>The receipt naming the durable entry the line became.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="line"/> is null.</exception>
    /// <remarks>
    /// Both paths go through the inbox, which is what a steer is: an entry that
    /// the kernel hands to the running turn at its next tool boundary, or queues
    /// as a follow-up when the turn ended first. The receipt is returned so the
    /// row this window drew can be joined to that entry.
    /// </remarks>
    public Task<InboxReceipt> SendAsync(string line, bool steer, CancellationToken cancellationToken = default) =>
        SendAsync(line, line, steer, cancellationToken);

    /// <summary>Sends a line whose visible text differs from what the model reads.</summary>
    /// <param name="line">The line as the person typed it.</param>
    /// <param name="submit">The line with resolved attachment references.</param>
    /// <param name="steer">Whether this is guidance for the running turn.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>The receipt naming the durable entry the line became.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either text is null.</exception>
    public Task<InboxReceipt> SendAsync(string line, string submit, bool steer, CancellationToken cancellationToken = default)
    {
        if (line is null)
        {
            throw new ArgumentNullException(nameof(line));
        }

        if (submit is null)
        {
            throw new ArgumentNullException(nameof(submit));
        }

        var intent = steer ? SubmitRequest.IntentSteer : SubmitRequest.IntentFollowUp;
        return _client.EnqueueAsync(new SubmitRequest(line, submit, intent), cancellationToken);
    }

    /// <summary>
    /// Reads the session's queue as the kernel holds it.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Every waiting entry, with the pause and both limits.</returns>
    public Task<QueueSnapshot> QueueAsync(CancellationToken cancellationToken = default)
    {
        return _client.QueueAsync(cancellationToken);
    }

    /// <summary>
    /// Takes one queued line back, returning its text.
    /// </summary>
    /// <param name="id">The entry the queue listed.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The text the entry carried, ready to be put back in the composer.</returns>
    /// <remarks>
    /// The read comes before the cancel: once the entry is gone nothing else holds
    /// the text, and a line that cannot be read stays queued rather than lost.
    /// </remarks>
    public async Task<string> WithdrawAsync(string id, CancellationToken cancellationToken = default)
    {
        var entry = await _client.ReadQueuedAsync(id, cancellationToken).ConfigureAwait(false);
        await _client.CancelQueuedAsync(id, cancellationToken).ConfigureAwait(false);
        return entry.Envelope.Readable;
    }

    /// <summary>
    /// Moves one queued line to the head and stops the turn holding the session.
    /// </summary>
    /// <param name="id">The entry to send now.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once the kernel has both accepted.</returns>
    /// <remarks>
    /// The move is what makes it next; the cancel is what makes "now" possible.
    /// Both are asked of the kernel, in that order, because a stop without the
    /// move would let whatever was ahead go first.
    /// </remarks>
    public async Task SendNowAsync(string id, CancellationToken cancellationToken = default)
    {
        await _client.MoveQueuedAsync(id, 0, cancellationToken).ConfigureAwait(false);
        await _client.CancelTurnAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Holds the queue, or lets it dispatch again.
    /// </summary>
    /// <param name="paused">True to hold dispatch, false to release it.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once the kernel recorded the state.</returns>
    public Task SetQueuePausedAsync(bool paused, CancellationToken cancellationToken = default)
    {
        return _client.SetQueuePausedAsync(paused, cancellationToken);
    }

    /// <summary>
    /// Asks the kernel to retry a blocked entry.
    /// </summary>
    /// <param name="id">The entry to retry.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once the kernel accepted the retry.</returns>
    public Task RetryQueuedAsync(string id, CancellationToken cancellationToken = default)
    {
        return _client.RetryQueuedAsync(id, cancellationToken);
    }

    /// <summary>
    /// Answers one approval the kernel is waiting on.
    /// </summary>
    /// <param name="id">The approval the kernel named.</param>
    /// <param name="allow">Whether the call may run.</param>
    /// <param name="session">Whether the same answer applies for the rest of this session.</param>
    /// <param name="persist">Whether the answer is remembered beyond this session.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>A task that completes once the kernel accepted the verdict.</returns>
    public Task ApproveAsync(
        string id,
        bool allow,
        bool session,
        bool persist,
        CancellationToken cancellationToken = default)
    {
        return _client.ApproveAsync(id, allow, session, persist, cancellationToken);
    }

    /// <summary>
    /// Lists the conversations the session directory holds.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The conversations, as the kernel orders them.</returns>
    public Task<IReadOnlyList<SessionEntry>> SessionsAsync(CancellationToken cancellationToken = default)
    {
        return _client.SessionsAsync(cancellationToken);
    }

    /// <summary>
    /// Reads the sidebar's tree: every project and the conversations under it.
    /// </summary>
    /// <param name="window">The page to ask for, or null for the default window.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The projects, as the kernel orders them.</returns>
    public Task<IReadOnlyList<TreeWorkspace>> TreeAsync(
        TreeWindow? window = null,
        CancellationToken cancellationToken = default)
    {
        return _client.TreeAsync(window, cancellationToken);
    }

    /// <summary>
    /// Pins or unpins one conversation.
    /// </summary>
    /// <param name="path">The transcript's path, as the tree listed it.</param>
    /// <param name="pinned">True to pin, false to unpin.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once the kernel recorded the pin.</returns>
    public Task PinSessionAsync(string path, bool pinned, CancellationToken cancellationToken = default)
    {
        return _client.PinSessionAsync(path, pinned, cancellationToken);
    }

    /// <summary>
    /// Archives or unarchives one conversation.
    /// </summary>
    /// <param name="path">The transcript's path, as the tree listed it.</param>
    /// <param name="archived">True to archive, false to bring it back.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once the kernel recorded the change.</returns>
    public Task ArchiveSessionAsync(string path, bool archived, CancellationToken cancellationToken = default)
    {
        return _client.ArchiveSessionAsync(path, archived, cancellationToken);
    }

    /// <summary>
    /// Marks one conversation seen, which clears its unread dot.
    /// </summary>
    /// <param name="path">The transcript's path, as the tree listed it.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once the kernel recorded the moment.</returns>
    public Task MarkSessionViewedAsync(string path, CancellationToken cancellationToken = default)
    {
        return _client.MarkSessionViewedAsync(path, cancellationToken);
    }

    /// <summary>
    /// Opens a conversation the session directory holds and re-reads its transcript.
    /// </summary>
    /// <param name="path">The transcript's path, as the listing reported it.</param>
    /// <param name="cancellationToken">Cancels the switch.</param>
    /// <returns>A task that completes once the new conversation is on screen.</returns>
    /// <remarks>
    /// The switch replaces the transcript rather than appending to it: the rows on
    /// screen belong to the conversation that was open, and a stream still folding
    /// frames for it would otherwise land under the new one's rows.
    /// </remarks>
    public async Task ResumeAsync(string path, CancellationToken cancellationToken = default)
    {
        await _client.ResumeAsync(path, cancellationToken).ConfigureAwait(false);
        Generation = new();
        Publish(new SessionState());
        await LoadHistoryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts a conversation with nothing in it and opens on the empty page.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes once the new session is on screen.</returns>
    /// <remarks>
    /// The same replacement as a switch, for the same reason: the rows belong to
    /// the conversation that was open, and a stream still folding frames for it
    /// would otherwise land under the new one's (empty) page.
    /// </remarks>
    public async Task NewSessionAsync(CancellationToken cancellationToken = default)
    {
        await _client.NewSessionAsync(cancellationToken).ConfigureAwait(false);
        Generation = new();
        Publish(new SessionState());
        await LoadHistoryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Copies the most recent part of a conversation into a new transcript.
    /// </summary>
    /// <param name="path">The transcript whose history is past the read limit.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The new transcript and how much of the original it left behind.</returns>
    /// <remarks>
    /// This does not open the copy: the caller decides when, because the offer it
    /// answers is a question the person may decline.
    /// </remarks>
    public Task<SessionTail> TailAsync(string path, CancellationToken cancellationToken = default)
    {
        return _client.TailAsync(path, cancellationToken);
    }

    /// <summary>
    /// Reads a call's whole output back from the kernel, a page at a time.
    /// </summary>
    /// <param name="callId">The call to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The output the kernel holds, as far as it could be read.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="callId"/> is empty.</exception>
    /// <remarks>
    /// A card whose middle the page let go of asks for this. The pages are read in
    /// turn and joined here, because the card is where the text is kept: nothing
    /// else holds a second copy of an output the kernel already has.
    ///
    /// The walk stops at <see cref="FullOutputPages"/> pages. Four megabytes is
    /// more than a person reads in a card, and past it the rest stays in the
    /// kernel rather than being pulled into memory that will never be drawn.
    /// </remarks>
    public async Task<string> ReadFullOutputAsync(string callId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(callId))
        {
            throw new ArgumentException("a call's output is read by the id the kernel named", nameof(callId));
        }

        var text = new System.Text.StringBuilder();
        var offset = 0;
        for (var page = 0; page < FullOutputPages; page++)
        {
            var window = await _client.ToolOutputAsync(callId, offset, FullOutputPageBytes, cancellationToken).ConfigureAwait(false);
            text.Append(window.Text);
            offset = window.Offset + System.Text.Encoding.UTF8.GetByteCount(window.Text);
            if (!window.Truncated)
            {
                break;
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Closes the conversation, the stream and whatever the launch owns behind it.
    /// </summary>
    /// <returns>A task that completes once everything is stopped.</returns>
    /// <remarks>
    /// Idempotent: a window closing and a host shutting down are two owners of one
    /// lifetime, and the second call must be as quiet as the first. Without this the
    /// second release found the cancellation source already disposed and threw from
    /// inside a close handler, where nothing can report it.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _closing.CancelAsync().ConfigureAwait(false);
        if (_release is not null)
        {
            await _release().ConfigureAwait(false);
        }

        _closing.Dispose();
    }

    /// <summary>
    /// Builds the id a restored row carries, from the session index it occupies.
    /// </summary>
    /// <param name="msgIndex">The session index the kernel reported.</param>
    /// <returns>The row's identity.</returns>
    private static string HistoryId(int msgIndex)
    {
        return "h" + msgIndex.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Folds one accepted frame and tells the window.
    /// </summary>
    /// <param name="frame">The frame the subscription admitted.</param>
    private void Fold(WireFrame frame)
    {
        SessionState next;
        lock (_sync)
        {
            next = _reducer.Reduce(_state, frame);
            Generation.Observe(frame);
            _state = next;
        }

        Changed?.Invoke(next);

        // The queue frame says only that the queue moved; whoever draws it asks
        // the kernel what it holds now.
        FrameReceived?.Invoke(this, frame);
        if (frame.Kind == "inbox_changed")
        {
            QueueChanged?.Invoke();
        }

        if (frame.Kind == "todo_progress")
        {
            _ = RefreshPlanAsync();
        }
    }

    /// <summary>
    /// Records a hole the stream could not close.
    /// </summary>
    /// <remarks>
    /// A hole is the transport's own fact. What a view does about it — re-read
    /// history, say it is behind — is decided there, so this only counts it.
    /// </remarks>
    private void ReportGap()
    {
        Interlocked.Increment(ref _gaps);
        HistoryInvalidated?.Invoke(this);
    }

    /// <summary>
    /// Replaces the state and tells the window.
    /// </summary>
    /// <param name="state">The state to publish.</param>
    private void Publish(SessionState state)
    {
        lock (_sync)
        {
            _state = state;
        }

        Changed?.Invoke(state);
    }

    /// <summary>
    /// Reads the tree's own stream and reports each change it announces.
    /// </summary>
    /// <param name="transport">The tree's transport.</param>
    /// <returns>A task that completes when the stream ends.</returns>
    /// <remarks>
    /// A failure here is not the conversation's: the sidebar simply stops
    /// hearing about changes until the window opens again, and the transcript
    /// keeps streaming.
    /// </remarks>
    private async Task WatchTreeAsync(SseEventTransport transport)
    {
        try
        {
            await foreach (var frame in transport.StreamAsync(() => null, _closing.Token).ConfigureAwait(false))
            {
                if (frame.Kind != "notice" || frame.Payload.ValueKind != System.Text.Json.JsonValueKind.Object)
                {
                    continue;
                }

                if (!frame.Payload.TryGetProperty("code", out var code)
                    || code.ValueKind != System.Text.Json.JsonValueKind.String
                    || code.GetString() != "session_tree_changed")
                {
                    continue;
                }

                TreeChanged?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            // Closing is the outcome asked for.
        }
        catch (Exception)
        {
            // The tree's stream is its own concern; the conversation keeps going.
        }
    }
}
