using Reasonix.Studio.State.Transcript;
using System.Linq;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the split a folded turn is drawn from.
/// </summary>
/// <remarks>
/// A turn folds its intermediate work under one line and keeps its last answer
/// visible. The split is the whole rule, so it is held here rather than through a
/// rendered window: a page that guessed where a turn ended would fold the answer
/// it was meant to show.
/// </remarks>
public sealed class TurnSegmentTests
{
    /// <summary>
    /// Checks that the work between a person's line and the last answer is the
    /// part that folds, and the answer with what follows it is the part that stays.
    /// </summary>
    [Fact]
    public void TheAnswerAndWhatFollowsStayOutOfTheFold()
    {
        var segment = Assert.Single(TurnSegment.Split(new TranscriptItem[]
        {
            new UserItem { Id = "u1", Text = "question" },
            new ToolItem { Id = "t1", Call = Call("read_file") },
            new SayItem { Id = "s1", Text = "thinking out loud" },
            new ToolItem { Id = "t2", Call = Call("bash") },
            new SayItem { Id = "s2", Text = "the answer" },
            new NoticeItem { Id = "n1", Level = "info", Text = "note" },
        }));

        Assert.NotNull(segment.Lead);
        Assert.Equal(new[] { "t1", "s1", "t2" }, segment.Work.Select(row => row.Id));
        Assert.Equal(new[] { "s2", "n1" }, segment.Tail.Select(row => row.Id));
        Assert.True(segment.HasWork);
    }

    /// <summary>
    /// Checks that a new line from the person starts a new turn, so one turn's
    /// work never folds under another turn's head.
    /// </summary>
    [Fact]
    public void EachLineThePersonSendsStartsATurn()
    {
        var segments = TurnSegment.Split(new TranscriptItem[]
        {
            new UserItem { Id = "u1", Text = "first" },
            new ToolItem { Id = "t1", Call = Call("read_file") },
            new SayItem { Id = "s1", Text = "answer one" },
            new UserItem { Id = "u2", Text = "second" },
            new ToolItem { Id = "t2", Call = Call("bash") },
            new SayItem { Id = "s2", Text = "answer two" },
        });

        Assert.Equal(2, segments.Count);
        Assert.Equal("u1", segments[0].Lead?.Id);
        Assert.Equal("u2", segments[1].Lead?.Id);
        Assert.Equal(new[] { "t2" }, segments[1].Work.Select(row => row.Id));
    }

    /// <summary>
    /// Checks that a turn with no answer yet keeps everything as work, which is
    /// what a turn still running looks like.
    /// </summary>
    [Fact]
    public void ATurnWithNoAnswerYetIsAllWork()
    {
        var segment = Assert.Single(TurnSegment.Split(new TranscriptItem[]
        {
            new UserItem { Id = "u1", Text = "question" },
            new ToolItem { Id = "t1", Call = Call("bash") },
        }));

        Assert.Equal(new[] { "t1" }, segment.Work.Select(row => row.Id));
        Assert.Empty(segment.Tail);
    }

    /// <summary>
    /// Checks that rows before the first line the person sent still form a
    /// segment, so a resumed transcript that opens mid-turn loses nothing.
    /// </summary>
    [Fact]
    public void RowsBeforeTheFirstLineStillFormASegment()
    {
        var segments = TurnSegment.Split(new TranscriptItem[]
        {
            new SayItem { Id = "s0", Text = "earlier" },
            new UserItem { Id = "u1", Text = "question" },
            new SayItem { Id = "s1", Text = "answer" },
        });

        Assert.Equal(2, segments.Count);
        Assert.Null(segments[0].Lead);
        Assert.Equal(new[] { "s0" }, segments[0].Tail.Select(row => row.Id));
    }

    /// <summary>
    /// Builds a bare call.
    /// </summary>
    /// <param name="name">The tool's name.</param>
    /// <returns>The call.</returns>
    private static Contracts.Wire.Tool Call(string name)
    {
        return new Contracts.Wire.Tool { ID = name, Name = name, ReadOnly = true };
    }
}
