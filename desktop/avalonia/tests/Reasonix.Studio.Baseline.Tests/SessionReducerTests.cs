using Reasonix.Studio.Contracts.Events;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System.Linq;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the reducer's rules: what a chunk belongs to, how a turn ends, and what
/// a repeated notice means.
/// </summary>
/// <remarks>
/// Each case names the defect its opposite would be, because these rules are the
/// difference between a transcript that matches the turn and one that reads as a
/// quiet gap where a result should be.
/// </remarks>
public sealed class SessionReducerTests
{
    /// <summary>
    /// Checks that a second answer opens its own row instead of appending to the
    /// first, which a turn with two model messages would otherwise produce.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async System.Threading.Tasks.Task AToolCallBetweenTwoAnswersOpensASecondRow()
    {
        var (reducer, _) = NewReducer();
        var state = new SessionState();

        state = await Fold(reducer, state, "{\"kind\":\"text\",\"text\":\"first\"}");
        state = await Fold(reducer, state, "{\"kind\":\"tool_dispatch\",\"tool\":{\"id\":\"c1\",\"name\":\"read_file\"}}");
        state = await Fold(reducer, state, "{\"kind\":\"text\",\"text\":\"second\"}");

        var says = state.Items.OfType<SayItem>().ToList();
        Assert.Equal(2, says.Count);
        Assert.Equal("first", says[0].Text);
        Assert.Equal("second", says[1].Text);
    }

    /// <summary>
    /// Checks that reasoning and answer text land on the same row, because one
    /// model message carries both.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async System.Threading.Tasks.Task ReasoningAndTextShareOneRow()
    {
        var (reducer, _) = NewReducer();
        var state = new SessionState();

        state = await Fold(reducer, state, "{\"kind\":\"reasoning\",\"text\":\"thinking\"}");
        state = await Fold(reducer, state, "{\"kind\":\"text\",\"text\":\"answer\"}");

        var say = Assert.Single(state.Items.OfType<SayItem>());
        Assert.Equal("thinking", say.Reasoning);
        Assert.Equal("answer", say.Text);
    }

    /// <summary>
    /// Checks that a turn's end settles every open message, so no caret keeps
    /// blinking on text nothing will add to.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async System.Threading.Tasks.Task TurnDoneSealsEveryOpenMessage()
    {
        var (reducer, _) = NewReducer();
        var state = new SessionState();

        state = await Fold(reducer, state, "{\"kind\":\"turn_started\"}");
        state = await Fold(reducer, state, "{\"kind\":\"text\",\"text\":\"partial\"}");
        state = await Fold(reducer, state, "{\"kind\":\"turn_done\",\"outcome\":\"\"}");

        Assert.All(state.Items.OfType<SayItem>(), say => Assert.True(say.Done));
        Assert.False(state.Running);
        Assert.IsType<CompletedTerminal>(state.Terminal);
    }

    /// <summary>
    /// Checks that a cancelled turn reads as stopped rather than failed: the
    /// kernel reports an error on cancellation, and folding the two together
    /// would show a person their own stop as a fault.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async System.Threading.Tasks.Task ACancelledTurnIsNotAFailure()
    {
        var (reducer, _) = NewReducer();
        var state = new SessionState();

        state = await Fold(reducer, state, "{\"kind\":\"turn_started\"}");
        state = await Fold(reducer, state, "{\"kind\":\"turn_done\",\"cancelled\":true,\"err\":\"cancelled\"}");

        Assert.IsType<CancelledTerminal>(state.Terminal);
        Assert.Equal("common.cancelled", state.Doing);
    }

    /// <summary>
    /// Checks that a repeated notice folds into the row already there instead of
    /// stacking, because a retry says the same thing each attempt.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async System.Threading.Tasks.Task ARepeatedNoticeFoldsIntoOneRow()
    {
        var (reducer, _) = NewReducer();
        var state = new SessionState();

        state = await Fold(reducer, state, "{\"kind\":\"notice\",\"level\":\"warn\",\"text\":\"retrying\",\"code\":\"retry\"}");
        state = await Fold(reducer, state, "{\"kind\":\"notice\",\"level\":\"warn\",\"text\":\"retrying\",\"code\":\"retry\"}");
        state = await Fold(reducer, state, "{\"kind\":\"notice\",\"level\":\"warn\",\"text\":\"retrying\",\"code\":\"retry\"}");

        var notice = Assert.Single(state.Items.OfType<NoticeItem>());
        Assert.Equal(3, notice.Count);
    }

    /// <summary>
    /// Checks that streaming text does not bump the revision, because the
    /// transcript's composition did not change and every card-derived panel would
    /// otherwise recompute per chunk.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async System.Threading.Tasks.Task StreamingTextDoesNotBumpTheRevision()
    {
        var (reducer, _) = NewReducer();
        var state = new SessionState();

        state = await Fold(reducer, state, "{\"kind\":\"text\",\"text\":\"a\"}");
        var afterFirst = state.Revision;
        state = await Fold(reducer, state, "{\"kind\":\"text\",\"text\":\"b\"}");

        Assert.Equal(afterFirst, state.Revision);
    }

    /// <summary>
    /// Checks that a notice that arrived as a repeat still bumps the revision,
    /// because its row's count is part of the transcript's composition.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async System.Threading.Tasks.Task AFoldedNoticeBumpsTheRevision()
    {
        var (reducer, _) = NewReducer();
        var state = new SessionState();

        state = await Fold(reducer, state, "{\"kind\":\"notice\",\"level\":\"warn\",\"text\":\"retrying\"}");
        var afterFirst = state.Revision;
        state = await Fold(reducer, state, "{\"kind\":\"notice\",\"level\":\"warn\",\"text\":\"retrying\"}");

        Assert.True(state.Revision > afterFirst);
    }

    /// <summary>
    /// Builds a reducer with a fixed clock, so a case's timing is its own.
    /// </summary>
    /// <returns>The reducer and the clock it reads.</returns>
    private static (SessionReducer Reducer, long Clock) NewReducer()
    {
        var now = 1_700_000_000_000L;
        return (new SessionReducer(new IdSequence(), () => now), now);
    }

    /// <summary>
    /// Folds one frame written as JSON text.
    /// </summary>
    /// <param name="reducer">The reducer under test.</param>
    /// <param name="state">The state before the frame.</param>
    /// <param name="json">The frame's JSON.</param>
    /// <returns>The state after the frame.</returns>
    private static System.Threading.Tasks.Task<SessionState> Fold(SessionReducer reducer, SessionState state, string json)
    {
        Assert.True(WireFrame.TryParse(json, out var frame));
        return System.Threading.Tasks.Task.FromResult(reducer.Reduce(state, frame));
    }
}
