using Reasonix.Studio.Contracts.Events;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Events;

/// <summary>
/// Turns one transport's frames into the single ordered stream a reducer sees.
/// </summary>
/// <remarks>
/// Frames that matter are numbered, so a break in the numbers is how a client
/// notices it missed something rather than rendering the gap as a quiet turn.
/// Four rules hold the stream together, and each exists because its opposite was
/// a real defect in the browser client:
/// <list type="bullet">
/// <item>A frame at or before the cursor has already been folded, so it is
/// dropped — the replay and the live stream reach the same frame.</item>
/// <item>A jump forward is a hole, and is filled from the replay log before its
/// successor is delivered, so a result never arrives ahead of the dispatch it
/// answers.</item>
/// <item>Numbering that goes backwards is a restarted kernel, not a hole:
/// nothing else ever sends a lower number than the client holds.</item>
/// <item>A watermark states where the client should have reached; a watermark
/// ahead of the cursor is a frame lost at the end of a turn, which nothing else
/// would notice.</item>
/// </list>
/// </remarks>
public sealed class EventSubscription
{
    /// <summary>
    /// Stands in for "no position yet": zero is a real number the kernel can send,
    /// and a subscriber that has just attached cannot be told from one that saw the
    /// stream start if both read zero.
    /// </summary>
    private const long NoPosition = -1;

    /// <summary>
    /// Holds the frames and the replay the subscription reads from.
    /// </summary>
    private readonly IEventTransport _transport;

    /// <summary>
    /// Receives every frame the reducer should fold.
    /// </summary>
    private readonly Action<WireFrame> _onFrame;

    /// <summary>
    /// Called when a hole cannot be closed.
    /// </summary>
    private readonly Action _onGap;

    /// <summary>
    /// Frames taken while a snapshot read was in flight.
    /// </summary>
    private readonly List<WireFrame> _held = new List<WireFrame>();

    /// <summary>
    /// Guards the held list and the bootstrapping flag, which are read from the
    /// stream's loop and written from the bootstrap call.
    /// </summary>
    private readonly object _sync = new object();

    /// <summary>
    /// The last frame number folded, or <see cref="NoPosition"/> before the stream
    /// states one.
    /// </summary>
    private long _seen = NoPosition;

    /// <summary>
    /// Whether frames are being held for a snapshot to state its position.
    /// </summary>
    private bool _booting;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventSubscription"/> class.
    /// </summary>
    /// <param name="transport">Where the frames and the replay come from.</param>
    /// <param name="onFrame">
    /// Receives every frame the reducer should fold, exactly once and in order.
    /// </param>
    /// <param name="onGap">
    /// Called when a hole cannot be closed, so the caller can re-read a snapshot.
    /// The hole is the transport's fact; which read model returns to a snapshot is
    /// the caller's decision.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any argument is null.
    /// </exception>
    public EventSubscription(IEventTransport transport, Action<WireFrame> onFrame, Action onGap)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _onFrame = onFrame ?? throw new ArgumentNullException(nameof(onFrame));
        _onGap = onGap ?? throw new ArgumentNullException(nameof(onGap));
    }

    /// <summary>
    /// Gets the last frame number folded, or null when the stream has not stated a
    /// position yet.
    /// </summary>
    public long? Position
    {
        get
        {
            var seen = Interlocked.Read(ref _seen);
            return seen == NoPosition ? null : seen;
        }
    }

    /// <summary>
    /// Folds one frame, filling a hole first when the frame's number skips ahead.
    /// </summary>
    /// <param name="frame">The frame as the transport delivered it.</param>
    /// <param name="cancellationToken">Cancels the replay a hole triggers.</param>
    /// <returns>A task that completes once the frame and any frames before it are folded.</returns>
    public async Task AcceptAsync(WireFrame frame, CancellationToken cancellationToken = default)
    {
        if (frame.Kind == "stream_watermark")
        {
            await AcceptWatermarkAsync(frame, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (frame.Kind == "stream_gap")
        {
            // The hole reaches the caller either way, but the number it carries is
            // not a position this subscriber reached.
            if (!IsBooting())
            {
                RaiseTo(frame.Seq);
            }

            _onGap();
            return;
        }

        if (IsBooting())
        {
            lock (_sync)
            {
                _held.Add(frame);
            }

            return;
        }

        await DeliverAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts holding frames until <see cref="BootstrapAsync"/> states a position.
    /// </summary>
    /// <remarks>
    /// A bootstrapping subscription holds from before the transport is attached: a
    /// frame taken live here would be folded ahead of the replay's, which is the
    /// ordering the whole class exists to prevent.
    /// </remarks>
    public void BeginBootstrap()
    {
        lock (_sync)
        {
            _booting = true;
        }
    }

    /// <summary>
    /// Holds the stream while a read model takes its own snapshot, then resumes
    /// from the position that snapshot stands at.
    /// </summary>
    /// <param name="position">
    /// Reads the frame number the caller's snapshot is at least as new as. The
    /// number is the whole stream's, never one model's.
    /// </param>
    /// <param name="cancellationToken">Cancels the replay.</param>
    /// <returns>A task that completes once the held frames are folded.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="position"/> is null.</exception>
    public async Task BootstrapAsync(Func<Task<long>> position, CancellationToken cancellationToken = default)
    {
        if (position is null)
        {
            throw new ArgumentNullException(nameof(position));
        }

        long at;
        try
        {
            at = await position().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Nothing to resume onto. Holding the stream any longer would stall the
            // view rather than repair it, so it runs live and says it is behind.
            _onGap();
            await FlushAsync(null, cancellationToken).ConfigureAwait(false);
            return;
        }

        Interlocked.Exchange(ref _seen, at);
        await FlushAsync(at, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops holding frames, and folds the ones taken while held.
    /// </summary>
    /// <param name="resumeFrom">Where to resume, or null to fold what was held as-is.</param>
    /// <param name="cancellationToken">Cancels the replay.</param>
    /// <returns>A task that completes once the stream is live again.</returns>
    private async Task FlushAsync(long? resumeFrom, CancellationToken cancellationToken)
    {
        List<WireFrame> queued;
        lock (_sync)
        {
            _booting = false;
            queued = new List<WireFrame>(_held);
            _held.Clear();
        }

        // The replay runs before the held frames: what the snapshot and the replay
        // overlap on folds twice onto the same state, and what falls between them is
        // gone — so the read names where it stands and the stream resumes from there.
        if (resumeFrom is { } after)
        {
            await RecoverAsync(after, cancellationToken).ConfigureAwait(false);
        }

        foreach (var frame in queued)
        {
            await DeliverAsync(frame, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads the stream's own statement of where a subscriber attached, or of a
    /// position it has fallen behind.
    /// </summary>
    /// <param name="frame">The watermark frame.</param>
    /// <param name="cancellationToken">Cancels the replay the watermark may trigger.</param>
    /// <returns>A task that completes once the frame is handled.</returns>
    private async Task AcceptWatermarkAsync(WireFrame frame, CancellationToken cancellationToken)
    {
        var seen = Interlocked.Read(ref _seen);
        if (seen == NoPosition)
        {
            // The first watermark states where this subscriber attached: a live-only
            // subscription replays nothing before it, so nothing before it was lost.
            // A bootstrapping one takes its position from its snapshot instead, and
            // taking it here would baseline the cursor over frames already held.
            if (!IsBooting())
            {
                Interlocked.Exchange(ref _seen, frame.Seq);
            }

            return;
        }

        if (frame.Seq > seen)
        {
            await RecoverAsync(seen, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Folds one frame, dropping a repeat and filling a hole when its number skips.
    /// </summary>
    /// <param name="frame">The frame to fold.</param>
    /// <param name="cancellationToken">Cancels the replay a hole triggers.</param>
    /// <returns>A task that completes once the frame is folded or dropped.</returns>
    private async Task DeliverAsync(WireFrame frame, CancellationToken cancellationToken)
    {
        if (frame.IsNumbered)
        {
            var seen = Interlocked.Read(ref _seen);
            if (seen != NoPosition)
            {
                if (frame.Seq < seen)
                {
                    // A different stream, not a hole: the kernel restarted and is
                    // counting from one again.
                    Interlocked.Exchange(ref _seen, frame.Seq);
                    _onGap();
                    _onFrame(frame);
                    return;
                }

                if (frame.Seq <= seen)
                {
                    return;
                }

                if (frame.Seq > seen + 1)
                {
                    // The replay is asked for the hole, but this frame is not handed
                    // to it: when the log has already shed the range, the replay
                    // cannot produce it, and dropping it here would lose the one
                    // frame the client actually received. It is folded afterwards if
                    // the replay did not already fold it.
                    await RecoverAsync(seen, cancellationToken).ConfigureAwait(false);
                    if (Interlocked.Read(ref _seen) < frame.Seq)
                    {
                        Interlocked.Exchange(ref _seen, frame.Seq);
                        _onFrame(frame);
                    }

                    return;
                }
            }

            Interlocked.Exchange(ref _seen, frame.Seq);
        }

        _onFrame(frame);
    }

    /// <summary>
    /// Reads the frames after a position out of the replay log and folds them.
    /// </summary>
    /// <param name="after">The last frame number already folded.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A task that completes once the replay is folded or given up on.</returns>
    private async Task RecoverAsync(long after, CancellationToken cancellationToken)
    {
        ReplayResult result;
        try
        {
            result = await _transport.ReplayAsync(after, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The frames are gone and asking again would not bring them back. The
            // transcript is the one source that can still answer.
            _onGap();
            return;
        }

        foreach (var frame in result.Frames)
        {
            if (frame.IsNumbered)
            {
                var seen = Interlocked.Read(ref _seen);
                if (seen != NoPosition && frame.Seq <= seen)
                {
                    continue;
                }

                Interlocked.Exchange(ref _seen, frame.Seq);
            }

            _onFrame(frame);
        }

        if (!result.Complete)
        {
            _onGap();
        }
    }

    /// <summary>
    /// Reports whether the subscription is still waiting for a snapshot.
    /// </summary>
    /// <returns>True while frames are being held.</returns>
    private bool IsBooting()
    {
        lock (_sync)
        {
            return _booting;
        }
    }

    /// <summary>
    /// Moves the cursor forward to a watermark the stream states.
    /// </summary>
    /// <param name="seq">The position to move to.</param>
    private void RaiseTo(long seq)
    {
        var seen = Interlocked.Read(ref _seen);
        if (seen == NoPosition || seq > seen)
        {
            Interlocked.Exchange(ref _seen, seq);
        }
    }
}
