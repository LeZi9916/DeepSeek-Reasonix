using Reasonix.Studio.Contracts.Events;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Events;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.Globalization;
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
public sealed class SessionEngine : IAsyncDisposable
{
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
    {
        if (kernel is null)
        {
            throw new ArgumentNullException(nameof(kernel));
        }

        // One handler for both reads and the stream, so a connection is pooled
        // rather than rebuilt and the credential rides exactly one place: a second
        // handler would be a second place to keep correct.
        var handler = new Boundary.CredentialHandler(kernel.Cookie());
        var http = new HttpClient(handler)
        {
            BaseAddress = kernel.Origin,
            Timeout = Timeout.InfiniteTimeSpan,
        };

        _client = new KernelClient(http);
        var transport = new SseEventTransport(kernel.Origin.ToString(), http);
        _transport = transport;
        _subscription = new EventSubscription(transport, Fold, ReportGap);
        _release = () =>
        {
            // The transport closes the client's connections; the kernel then stops.
            transport.Dispose();
            kernel.Dispose();
            return ValueTask.CompletedTask;
        };
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionEngine"/> class over
    /// reads the caller supplied and therefore owns.
    /// </summary>
    /// <param name="client">Reads status and history, and submits lines.</param>
    /// <param name="transport">Carries the live stream and its replay.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="client"/> or <paramref name="transport"/> is null.
    /// </exception>
    public SessionEngine(IKernelClient client, IEventTransport transport)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _subscription = new EventSubscription(transport, Fold, ReportGap);
        _release = null;
    }

    /// <summary>
    /// Raised once per fold, with the state the fold produced.
    /// </summary>
    /// <remarks>
    /// Raised on the stream's own loop rather than marshalled here: a view decides
    /// where it may draw, and doing it here would decide that for every view.
    /// </remarks>
    public event Action<SessionState>? Changed;

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
    /// Reads the messages the session already holds, so a window opens on its
    /// transcript rather than on an empty page.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A task that completes once the history is folded.</returns>
    public async Task LoadHistoryAsync(CancellationToken cancellationToken = default)
    {
        var messages = await _client.HistoryAsync(cancellationToken).ConfigureAwait(false);
        if (messages.Count == 0)
        {
            return;
        }

        var items = new List<TranscriptItem>();
        foreach (var message in messages)
        {
            switch (message.Role)
            {
                case "user":
                    items.Add(new UserItem
                    {
                        Id = HistoryId(message.MsgIndex),
                        Text = message.Content,
                        AuthoredTurn = message.MsgIndex,
                        MsgIndex = message.MsgIndex,
                        At = message.CreatedAt == 0 ? null : message.CreatedAt,
                        Steer = message.Steer,
                    });
                    break;

                case "assistant":
                    items.Add(new SayItem
                    {
                        Id = HistoryId(message.MsgIndex),
                        Text = message.Content,
                        Reasoning = string.IsNullOrEmpty(message.Reasoning) ? null : message.Reasoning,
                        Source = string.IsNullOrEmpty(message.ModelRef) ? null : message.ModelRef,
                        Done = true,
                    });
                    break;
            }
        }

        // A restored transcript owes no entrance: those facts are not arriving, they
        // arrived. What the fold produces afterwards earns its own.
        Publish(State with
        {
            Items = items,
            EnteredThrough = items.Count,
            Revision = State.Revision + 1,
        });
    }

    /// <summary>
    /// Attaches the live stream and keeps it open until the conversation closes.
    /// </summary>
    /// <returns>A task that completes when the stream ends.</returns>
    public async Task StartAsync()
    {
        try
        {
            await foreach (var frame in _transport.StreamAsync(() => _subscription.Position, _closing.Token)
                .ConfigureAwait(false))
            {
                await _subscription.AcceptAsync(frame, _closing.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Closing is the outcome asked for.
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
        Publish(new SessionState());
        await LoadHistoryAsync(cancellationToken).ConfigureAwait(false);
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
            _state = next;
        }

        Changed?.Invoke(next);
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
}
