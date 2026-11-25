using Reasonix.Studio.Contracts.Wire;
using Reasonix.Studio.State.Transcript;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the four decorations the transcript draws between its rows.
/// </summary>
/// <remarks>
/// Each one is a rule about rows rather than a piece of drawing: when a group
/// opens, when two delegates read as a group of their own, where the unread line
/// belongs. Keeping them pure is what lets them be held without a window, and a
/// page that decided any of them itself would be a second answer to the same
/// question.
/// </remarks>
public sealed class TranscriptDecorationTests
{
    /// <summary>
    /// Checks that the five fold modes open exactly where each one says.
    /// </summary>
    [Fact]
    public void FoldModesOpenWhereTheySay()
    {
        Assert.False(FoldPolicy.StartsOpen(FoldMode.Folded, live: true, failed: true, changed: true));
        Assert.True(FoldPolicy.StartsOpen(FoldMode.Live, live: true));
        Assert.False(FoldPolicy.StartsOpen(FoldMode.Live, live: false));
        Assert.True(FoldPolicy.StartsOpen(FoldMode.Failed, failed: true));
        Assert.False(FoldPolicy.StartsOpen(FoldMode.Failed, live: true));
        Assert.True(FoldPolicy.StartsOpen(FoldMode.Changed, changed: true));
        Assert.False(FoldPolicy.StartsOpen(FoldMode.Changed, failed: true));
        Assert.True(FoldPolicy.StartsOpen(FoldMode.Open));
    }

    /// <summary>
    /// Checks that a stretch's tally counts calls and their reads rows together.
    /// </summary>
    [Fact]
    public void TheTallyCountsCallsAndReads()
    {
        var tally = ActivityTally.Of(new TranscriptItem[]
        {
            Tool("a", running: false),
            new ReadsItem { Id = "r", Tools = new[] { Call("b"), Call("c") } },
        });

        Assert.Equal(3, tally.Calls);
        Assert.Equal(0, tally.Running);
        Assert.True(tally.Settled);
    }

    /// <summary>
    /// Checks that running and failed calls are counted apart.
    /// </summary>
    [Fact]
    public void TheTallySeparatesRunningFromFailed()
    {
        var tally = ActivityTally.Of(new TranscriptItem[]
        {
            Tool("a", running: true),
            new ToolItem { Id = "b", Call = Call("b") with { Err = "boom" }, Running = false },
        });

        Assert.Equal(2, tally.Calls);
        Assert.Equal(1, tally.Running);
        Assert.Equal(1, tally.Failed);
        Assert.False(tally.Settled);
    }

    /// <summary>
    /// Checks that the span is wall time when every call reports its own
    /// timestamps, so concurrent work is not counted twice.
    /// </summary>
    [Fact]
    public void TheSpanIsWallTimeWhenEveryCallTimes()
    {
        var tally = ActivityTally.Of(new TranscriptItem[]
        {
            new ToolItem { Id = "a", Call = Call("a") with { StartedAt = 1000, EndedAt = 4000 }, Running = false },
            new ToolItem { Id = "b", Call = Call("b") with { StartedAt = 2000, EndedAt = 5000 }, Running = false },
        });

        Assert.Equal(4000, tally.Milliseconds);
    }

    /// <summary>
    /// Checks that the span falls back to the durations added up when a call does
    /// not say when it ran.
    /// </summary>
    [Fact]
    public void TheSpanIsTheSumWhenACallDoesNotTime()
    {
        var tally = ActivityTally.Of(new TranscriptItem[]
        {
            new ToolItem { Id = "a", Call = Call("a") with { DurationMs = 900 }, Running = false },
            new ToolItem { Id = "b", Call = Call("b") with { DurationMs = 600 }, Running = false },
        });

        Assert.Equal(1500, tally.Milliseconds);
    }

    /// <summary>
    /// Checks that two delegates sent out side by side take a lane of their own.
    /// </summary>
    [Fact]
    public void TwoDelegatesTakeALaneOfTheirOwn()
    {
        var lanes = TranscriptLanes.Split(new TranscriptItem[]
        {
            Say("hello"),
            Delegate("a"),
            Delegate("b"),
            Say("done"),
        });

        Assert.Equal(3, lanes.Count);
        Assert.False(lanes[0].IsDelegateGroup);
        Assert.True(lanes[1].IsDelegateGroup);
        Assert.Equal(2, lanes[1].Delegates.Count);
        Assert.False(lanes[2].IsDelegateGroup);
    }

    /// <summary>
    /// Checks that a lone delegate stays in the ordinary run, because one
    /// sub-agent is a call and not a fleet.
    /// </summary>
    [Fact]
    public void ALoneDelegateStaysInTheOrdinaryRun()
    {
        var lanes = TranscriptLanes.Split(new TranscriptItem[]
        {
            Say("hello"),
            Delegate("a"),
            Say("done"),
        });

        Assert.Single(lanes);
        Assert.Equal(3, lanes[0].Rows.Count);
        Assert.False(lanes[0].IsDelegateGroup);
    }

    /// <summary>
    /// Checks that a delegate's own steps are not read as delegates of their own,
    /// which is how one sub-agent doing many things once read as many sub-agents.
    /// </summary>
    [Fact]
    public void ADelegatesOwnStepsAreNotDelegates()
    {
        var child = Call("step") with
        {
            Profile = new Profile { Name = "reader" },
            ParentID = "a",
        };
        Assert.False(TranscriptLanes.IsDelegation(child));

        var lanes = TranscriptLanes.Split(new TranscriptItem[]
        {
            new ToolItem
            {
                Id = "a",
                Call = Call("a") with
                {
                    Profile = new Profile { Name = "reader" },
                },
                Running = false,
            },
            new ToolItem { Id = "step", Call = child, Running = false },
        });

        Assert.Single(lanes);
        Assert.False(lanes[0].IsDelegateGroup);
    }

    /// <summary>Splits a long ordinary stretch without copying every prefix.</summary>
    [Fact]
    public void ALongOrdinaryLaneAllocatesLinearly()
    {
        var rows = Enumerable.Range(0, 5000).Select(i => (TranscriptItem)Say("answer", i.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        _ = TranscriptLanes.Split(rows[..1]);

        var before = System.GC.GetAllocatedBytesForCurrentThread();
        var lane = Assert.Single(TranscriptLanes.Split(rows));
        var allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(rows, lane.Rows);
        Assert.InRange(allocated, 0, 512_000);
    }

    /// <summary>Uses the first authored turn after the cutoff, before its response.</summary>
    [Fact]
    public void TheUnreadLineGoesBeforeTheFirstUnseenTurn()
    {
        var items = new TranscriptItem[]
        {
            new UserItem { Id = "u1", Text = "first question", At = 10 },
            Say("seen", id: "s1"),
            new UserItem { Id = "u2", Text = "follow-up", At = 30 },
            Say("new", id: "s2"),
        };

        Assert.Equal(2, NewAnswerMarker.PlaceOf(items, 20));
    }

    /// <summary>When the prompt predates the cutoff, uses its first meaningful response.</summary>
    [Fact]
    public void TheUnreadLineSkipsWorkAndProvisionalMessages()
    {
        var items = new TranscriptItem[]
        {
            new UserItem { Id = "u1", Text = "first question", At = 10 },
            Say("seen", id: "s1"),
            new UserItem { Id = "u2", Text = "follow-up", At = 15 },
            Tool("t1", running: false),
            Say(" ", id: "blank"),
            new UserItem { Id = "steer", Text = "steering", At = 30, Steer = true },
            new UserItem { Id = "pending", Text = "queued", At = 30, Pending = true },
            new SayItem { Id = "thought", Reasoning = "next step" },
            Say("answer", id: "s2"),
        };

        Assert.Equal(7, NewAnswerMarker.PlaceOf(items, 20));
    }

    /// <summary>Omits the separator when there is no cutoff, no response, or nothing before it.</summary>
    [Fact]
    public void TheUnreadLineRequiresAnInternalBoundary()
    {
        var reply = new TranscriptItem[] { Say("one", id: "s1") };
        var work = new TranscriptItem[] { new UserItem { Id = "u1", Text = "first question", At = 10 }, Tool("t1", running: false) };
        Assert.Equal(-1, NewAnswerMarker.PlaceOf(reply, null));
        Assert.Equal(-1, NewAnswerMarker.PlaceOf(reply, 20));
        Assert.Equal(-1, NewAnswerMarker.PlaceOf(work, 20));
        Assert.Equal(-1, NewAnswerMarker.PlaceOf(work, 5));
    }

    /// <summary>Holds a divider in place through another turn and rearms it for a new unread span.</summary>
    [Fact]
    public void TheUnreadLineStaysAtTheOldAnswerWhenANewTurnStarts()
    {
        TranscriptItem[] initial = [new UserItem { Id = "u1", Text = "first question", At = 10 }, Say("answer", id: "s1")];
        TranscriptItem[] next = [.. initial, new UserItem { Id = "u2", Text = "follow-up", At = 30 }, Say("follow-up", id: "s2")];
        var boundary = new NewAnswerBoundary();
        boundary.Remember(20);

        Assert.Null(boundary.Resolve([], running: false));
        Assert.Equal("s1", boundary.Resolve(initial, running: false));
        Assert.Equal("s1", boundary.Resolve(next, running: true));
        boundary.Remember(20);
        Assert.Equal("s1", boundary.Resolve(next, running: false));
        boundary.Remember(25);
        Assert.Equal("u2", boundary.Resolve(next, running: false));
    }

    /// <summary>
    /// Builds one settled call row.
    /// </summary>
    /// <param name="name">The tool's name.</param>
    /// <param name="running">Whether it is still running.</param>
    /// <returns>The row.</returns>
    private static ToolItem Tool(string name, bool running)
    {
        return new ToolItem { Id = name, Call = Call(name), Running = running };
    }

    /// <summary>
    /// Builds one answer row.
    /// </summary>
    /// <param name="text">What it says.</param>
    /// <param name="id">Its identity.</param>
    /// <returns>The row.</returns>
    private static SayItem Say(string text, string id = "say")
    {
        return new SayItem { Id = id, Text = text, Done = true };
    }

    /// <summary>
    /// Builds one delegate row.
    /// </summary>
    /// <param name="name">The tool's name.</param>
    /// <returns>The row, carrying a profile so it reads as a delegation.</returns>
    private static ToolItem Delegate(string name)
    {
        return new ToolItem
        {
            Id = name,
            Call = Call(name) with { Profile = new Profile { Name = "reader" } },
            Running = false,
        };
    }

    /// <summary>
    /// Builds a bare call.
    /// </summary>
    /// <param name="name">The tool's name.</param>
    /// <returns>The call.</returns>
    private static Tool Call(string name)
    {
        return new Tool { ID = name, Name = name, ReadOnly = true };
    }
}
