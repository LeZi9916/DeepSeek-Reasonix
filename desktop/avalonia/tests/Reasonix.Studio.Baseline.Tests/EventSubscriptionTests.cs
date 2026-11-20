using Reasonix.Studio.Contracts.Events;
using Reasonix.Studio.Kernel.Events;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the subscription's ordering rules: what a repeat, a hole, a restarted
/// kernel and a watermark each mean to a reducer.
/// </summary>
/// <remarks>
/// These are the rules that decide whether a transcript shows a result before the
/// call it answers, or renders a lost frame as a quiet turn. Each case names the
/// defect its opposite would be.
/// </remarks>
public sealed class EventSubscriptionTests
{
    /// <summary>
    /// Checks that a frame already folded is not folded again, because the replay
    /// and the live stream reach the same frame.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task ARepeatedFrameIsFoldedOnce()
    {
        var folded = new List<long>();
        var subscription = new EventSubscription(new FakeTransport(), f => folded.Add(f.Seq), () => { });

        await subscription.AcceptAsync(Frame(1));
        await subscription.AcceptAsync(Frame(1));

        Assert.Equal(new long[] { 1 }, folded);
    }

    /// <summary>
    /// Checks that an unnumbered frame is folded every time: it is a live delta,
    /// not a position, so repeating one is not repeating a fact.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task AnUnnumberedFrameIsNeverDeduplicated()
    {
        var folded = new List<string>();
        var subscription = new EventSubscription(new FakeTransport(), f => folded.Add(f.Kind), () => { });

        await subscription.AcceptAsync(Frame(0, "text"));
        await subscription.AcceptAsync(Frame(0, "text"));

        Assert.Equal(new[] { "text", "text" }, folded);
    }

    /// <summary>
    /// Checks that a hole is filled from the replay log before its successor is
    /// folded, so a result never arrives ahead of the dispatch it answers.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task AHoleIsFilledBeforeTheFrameThatFollowsIt()
    {
        var folded = new List<long>();
        var transport = new FakeTransport
        {
            Replay = after => new ReplayResult(new[] { Frame(2), Frame(3) }, true, 3),
        };
        var subscription = new EventSubscription(transport, f => folded.Add(f.Seq), () => { });

        await subscription.AcceptAsync(Frame(1));
        await subscription.AcceptAsync(Frame(4));

        Assert.Equal(new long[] { 1, 2, 3, 4 }, folded);
        Assert.Equal(new long[] { 1 }, transport.ReplayedAfter);
    }

    /// <summary>
    /// Checks that the frame which revealed the hole is still folded when the
    /// replay could not reach back far enough, because it is the one frame the
    /// client actually received.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task TheFrameThatRevealedAHoleSurvivesAnIncompleteReplay()
    {
        var folded = new List<long>();
        var transport = new FakeTransport
        {
            Replay = _ => new ReplayResult(Array.Empty<WireFrame>(), false, 9),
        };
        var gaps = 0;
        var subscription = new EventSubscription(transport, f => folded.Add(f.Seq), () => gaps += 1);

        await subscription.AcceptAsync(Frame(1));
        await subscription.AcceptAsync(Frame(9));

        Assert.Equal(new long[] { 1, 9 }, folded);
        Assert.Equal(1, gaps);
    }

    /// <summary>
    /// Checks that numbering which goes backwards reads as a restarted kernel
    /// rather than a hole, and that the frame still reaches the reducer.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task BackwardsNumberingReportsAGapAndFoldsTheFrame()
    {
        var folded = new List<long>();
        var gaps = 0;
        var subscription = new EventSubscription(new FakeTransport(), f => folded.Add(f.Seq), () => gaps += 1);

        await subscription.AcceptAsync(Frame(40));
        await subscription.AcceptAsync(Frame(1));

        Assert.Equal(new long[] { 40, 1 }, folded);
        Assert.Equal(1, gaps);
    }

    /// <summary>
    /// Checks that the first watermark states where the subscriber attached, and
    /// that a later one ahead of the cursor is read as a frame lost at the end of
    /// a turn.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task AWatermarkAheadOfTheCursorReadsAsALostFrame()
    {
        var folded = new List<long>();
        var transport = new FakeTransport
        {
            Replay = after => new ReplayResult(new[] { Frame(after + 1) }, true, after + 1),
        };
        var gaps = 0;
        var subscription = new EventSubscription(transport, f => folded.Add(f.Seq), () => gaps += 1);

        await subscription.AcceptAsync(Frame(5));
        await subscription.AcceptAsync(Frame(7, "stream_watermark"));

        Assert.Equal(new long[] { 5, 6 }, folded);
        Assert.Equal(0, gaps);
    }

    /// <summary>
    /// Checks that a bootstrapping subscription holds live frames until its
    /// snapshot names a position, then replays from there before folding them.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task AFrameTakenDuringBootstrapWaitsForTheReplay()
    {
        var folded = new List<long>();
        var transport = new FakeTransport
        {
            Replay = after => new ReplayResult(new[] { Frame(after + 1) }, true, after + 1),
        };
        var subscription = new EventSubscription(transport, f => folded.Add(f.Seq), () => { });

        subscription.BeginBootstrap();
        await subscription.AcceptAsync(Frame(30));
        await subscription.BootstrapAsync(() => Task.FromResult(10L));

        // The replay fills 11, then the held frame folds. 30 skips ahead, so the
        // transport answers the hole it names rather than the held frame arriving
        // first.
        Assert.Equal(11, folded[0]);
        Assert.Contains(30, folded);
    }

    /// <summary>
    /// Builds one frame for a case.
    /// </summary>
    /// <param name="seq">The frame's position, or 0 for a live-only frame.</param>
    /// <param name="kind">The frame's kind.</param>
    /// <returns>The parsed frame.</returns>
    private static WireFrame Frame(long seq, string kind = "text")
    {
        var json = seq > 0
            ? $"{{\"kind\":\"{kind}\",\"seq\":{seq}}}"
            : $"{{\"kind\":\"{kind}\"}}";

        Assert.True(WireFrame.TryParse(json, out var frame));
        return frame;
    }

    /// <summary>
    /// Answers the two reads without a socket, so the ordering rules can be tested
    /// on their own.
    /// </summary>
    private sealed class FakeTransport : IEventTransport
    {
        /// <summary>
        /// Gets or sets what a replay answers for a position.
        /// </summary>
        public Func<long, ReplayResult> Replay
        {
            get; set;
        } =
            after => new ReplayResult(Array.Empty<WireFrame>(), true, after);

        /// <summary>
        /// Gets the positions a replay was asked for, in order.
        /// </summary>
        public List<long> ReplayedAfter { get; } = new List<long>();

        /// <inheritdoc/>
        public IAsyncEnumerable<WireFrame> StreamAsync(Func<long?> cursor, CancellationToken cancellationToken)
        {
            return Empty();
        }

        /// <inheritdoc/>
        public Task<ReplayResult> ReplayAsync(long after, CancellationToken cancellationToken)
        {
            ReplayedAfter.Add(after);
            return Task.FromResult(Replay(after));
        }

        /// <summary>
        /// Yields nothing: these cases drive the subscription frame by frame.
        /// </summary>
        /// <returns>An empty sequence.</returns>
        private static async IAsyncEnumerable<WireFrame> Empty()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
