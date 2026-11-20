using Reasonix.Studio.Contracts.Events;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the seven rows the transcript draws besides the message and call rows.
/// </summary>
/// <remarks>
/// Each of these exists because the kernel said something an ordinary row cannot
/// carry: a decision the person made, a question waiting on them, a fold, a saved
/// fact, a duration, or a surface an extension published. A reducer that dropped
/// one of them would leave the turn looking like it did less than it did.
/// </remarks>
public sealed class SessionReducerRowTests
{
    /// <summary>
    /// Checks that a guardian assessment becomes its own row.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task AGuardianAssessmentBecomesARow()
    {
        var (reducer, _) = New();
        var state = await Fold(reducer, new SessionState(), "{\"kind\":\"guardian_assessment\",\"guardian\":{\"id\":\"g1\",\"tool\":\"bash\",\"outcome\":\"allow\"}}");

        var row = Assert.Single(state.Items.OfType<GuardianItem>());
        Assert.Equal("allow", row.Assessment.Outcome);
    }

    /// <summary>
    /// Checks that a question becomes its own row and says the turn is waiting.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task AQuestionBecomesARowAndWaits()
    {
        var (reducer, _) = New();
        var state = await Fold(reducer, new SessionState(), "{\"kind\":\"ask_request\",\"ask\":{\"id\":\"a1\",\"questions\":[]}}");

        var row = Assert.Single(state.Items.OfType<AskItem>());
        Assert.Equal("a1", row.Ask.ID);
        Assert.Null(row.Answered);
        Assert.Equal("app.waiting_for_your_answer", state.Doing);
    }

    /// <summary>
    /// Checks that a fold opens one row, grows its summary, and settles in place.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task AFoldOpensGrowsAndSettlesInPlace()
    {
        var (reducer, _) = New();
        var state = new SessionState();

        state = await Fold(reducer, state, "{\"kind\":\"compaction_started\",\"compaction\":{\"trigger\":\"size\"}}");
        state = await Fold(reducer, state, "{\"kind\":\"compaction_progress\",\"text\":\"summary so\"}");
        state = await Fold(reducer, state, "{\"kind\":\"compaction_progress\",\"text\":\" far\"}");

        var open = Assert.Single(state.Items.OfType<CompactionItem>());
        Assert.False(open.Done);
        Assert.Equal("summary so far", open.Compaction.Summary);

        state = await Fold(reducer, state, "{\"kind\":\"compaction_done\",\"compaction\":{\"messages\":12,\"summary\":\"summary so far\"}}");

        var settled = Assert.Single(state.Items.OfType<CompactionItem>());
        Assert.True(settled.Done);
        Assert.Equal(12, settled.Compaction.Messages);
        Assert.Equal(open.Id, settled.Id);
    }

    /// <summary>
    /// Checks that a chunk without text restarts the summary, because the
    /// attempt before it was discarded.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task AProgressFrameWithNoTextRestartsTheSummary()
    {
        var (reducer, _) = New();
        var state = new SessionState();

        state = await Fold(reducer, state, "{\"kind\":\"compaction_started\"}");
        state = await Fold(reducer, state, "{\"kind\":\"compaction_progress\",\"text\":\"first attempt\"}");
        state = await Fold(reducer, state, "{\"kind\":\"compaction_progress\"}");

        Assert.Equal(string.Empty, Assert.Single(state.Items.OfType<CompactionItem>()).Compaction.Summary);
    }

    /// <summary>
    /// Checks that a landed remember replaces its own call row with a card.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task ALandedRememberReplacesItsCallRow()
    {
        var (reducer, _) = New();
        var state = new SessionState();

        state = await Fold(reducer, state, "{\"kind\":\"tool_dispatch\",\"tool\":{\"id\":\"c1\",\"name\":\"remember\"}}");
        state = await Fold(
            reducer,
            state,
            "{\"kind\":\"tool_result\",\"tool\":{\"id\":\"c1\",\"name\":\"remember\",\"args\":\"{\\\"name\\\":\\\"build\\\",\\\"description\\\":\\\"run make check\\\",\\\"scope\\\":\\\"global\\\"}\"}}");

        var card = Assert.Single(state.Items.OfType<RememberItem>());
        Assert.Equal("build", card.Fact.Name);
        Assert.Equal("run make check", card.Fact.Description);
        Assert.Equal("global", card.Fact.Scope);
        Assert.Equal("relevant", card.Fact.Activation);
        Assert.Equal("build", card.Fact.Title);
        Assert.Empty(state.Items.OfType<ToolItem>());
    }

    /// <summary>
    /// Checks that a failed remember stays an ordinary failed call: a save that
    /// did not land is not a saved fact.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task AFailedRememberStaysAFailedCall()
    {
        var (reducer, _) = New();
        var state = new SessionState();

        state = await Fold(reducer, state, "{\"kind\":\"tool_dispatch\",\"tool\":{\"id\":\"c1\",\"name\":\"remember\"}}");
        state = await Fold(
            reducer,
            state,
            "{\"kind\":\"tool_result\",\"tool\":{\"id\":\"c1\",\"name\":\"remember\",\"err\":\"disk full\",\"args\":\"{\\\"name\\\":\\\"build\\\"}\"}}");

        Assert.Empty(state.Items.OfType<RememberItem>());
        Assert.Single(state.Items.OfType<ToolItem>());
    }

    /// <summary>
    /// Checks that a finished turn long enough to notice gets its own duration row.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task ATurnLongEnoughToNoticeGetsADurationRow()
    {
        var (reducer, _) = New();
        var state = await Fold(reducer, new SessionState(), "{\"kind\":\"turn_done\",\"workDurationMs\":65000,\"workStartedAt\":1700000000000}");

        var row = Assert.Single(state.Items.OfType<WorkedItem>());
        Assert.Equal(65000, row.Milliseconds);
        Assert.Equal(1700000000000, row.StartedAt);
    }

    /// <summary>
    /// Checks that a turn too short to notice gets no duration row.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task AShortTurnGetsNoDurationRow()
    {
        var (reducer, _) = New();
        var state = await Fold(reducer, new SessionState(), "{\"kind\":\"turn_done\",\"workDurationMs\":420}");

        Assert.Empty(state.Items.OfType<WorkedItem>());
    }

    /// <summary>
    /// Checks that consecutive lookups fold into one reads row and that the run
    /// keeps growing rather than starting a second one.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task ConsecutiveLookupsFoldIntoOneReadsRow()
    {
        var (reducer, _) = New();
        var state = new SessionState();

        state = await Settle(reducer, state, "read_file", "r1");
        state = await Settle(reducer, state, "read_file", "r2");
        state = await Settle(reducer, state, "grep", "g1");

        var run = Assert.Single(state.Items.OfType<ReadsItem>());
        Assert.Equal(3, run.Tools.Count);
        Assert.Empty(state.Items.OfType<ToolItem>());
    }

    /// <summary>
    /// Checks that a command between two lookups ends the run, so the two runs do
    /// not read as one uninterrupted stretch of reading.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task ACommandEndsAReadsRun()
    {
        var (reducer, _) = New();
        var state = new SessionState();

        state = await Settle(reducer, state, "read_file", "r1");
        state = await Settle(reducer, state, "read_file", "r2");
        state = await Settle(reducer, state, "bash", "b1");
        state = await Settle(reducer, state, "read_file", "r3");

        Assert.Single(state.Items.OfType<ReadsItem>());
        Assert.Equal(2, state.Items.OfType<ToolItem>().Count());
    }

    /// <summary>
    /// Checks that two writes in a row keep their own rows, because a write is not
    /// a lookup and a count of them says nothing about what changed.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task TwoWritesKeepTheirOwnRows()
    {
        var (reducer, _) = New();
        var state = new SessionState();

        state = await Settle(reducer, state, "edit_file", "w1");
        state = await Settle(reducer, state, "edit_file", "w2");

        Assert.Empty(state.Items.OfType<ReadsItem>());
        Assert.Equal(2, state.Items.OfType<ToolItem>().Count());
    }

    /// <summary>
    /// Checks that an extension surface lands once and moves in place when the
    /// same surface is published again.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task ASurfaceMovesInPlaceWhenRepublished()
    {
        var (reducer, _) = New();
        var state = new SessionState();

        state = await Fold(reducer, state, "{\"kind\":\"extension_surface\",\"extension\":{\"code\":\"e1\",\"pluginId\":\"p\",\"surfaceId\":\"s\",\"kind\":\"card\"}}");
        var first = Assert.Single(state.Items.OfType<ExtensionItem>());

        state = await Fold(reducer, state, "{\"kind\":\"extension_status\",\"extension\":{\"code\":\"e2\",\"pluginId\":\"p\",\"surfaceId\":\"s\",\"kind\":\"card\",\"status\":{\"label\":\"working\"}}}");
        var second = Assert.Single(state.Items.OfType<ExtensionItem>());

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("e2", second.Surface.Code);
    }

    /// <summary>
    /// Checks that a surface with no code is the extension taking its card away.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task ASurfaceWithNoCodeIsRemoved()
    {
        var (reducer, _) = New();
        var state = new SessionState();

        state = await Fold(reducer, state, "{\"kind\":\"extension_surface\",\"extension\":{\"code\":\"e1\",\"pluginId\":\"p\",\"surfaceId\":\"s\",\"kind\":\"card\"}}");
        state = await Fold(reducer, state, "{\"kind\":\"extension_status\",\"extension\":{\"pluginId\":\"p\",\"surfaceId\":\"s\"}}");

        Assert.Empty(state.Items.OfType<ExtensionItem>());
    }

    /// <summary>
    /// Builds a reducer over a fixed clock and a counter.
    /// </summary>
    /// <returns>The reducer and the identity sequence it mints from.</returns>
    private static (SessionReducer Reducer, IdSequence Ids) New()
    {
        var ids = new IdSequence();
        return (new SessionReducer(ids, () => 1_700_000_000_000), ids);
    }

    /// <summary>
    /// Folds one frame given as JSON.
    /// </summary>
    /// <param name="reducer">The reducer.</param>
    /// <param name="state">The state before the frame.</param>
    /// <param name="json">The frame's body.</param>
    /// <returns>The state after the frame.</returns>
    private static Task<SessionState> Fold(SessionReducer reducer, SessionState state, string json)
    {
        Assert.True(WireFrame.TryParse(json, out var frame));
        return Task.FromResult(reducer.Reduce(state, frame));
    }

    /// <summary>
    /// Dispatches and settles one call, which is the sequence a real stream sends.
    /// </summary>
    /// <param name="reducer">The reducer.</param>
    /// <param name="state">The state before the call.</param>
    /// <param name="name">The tool's name.</param>
    /// <param name="id">The call's id.</param>
    /// <returns>The state after the call settled.</returns>
    private static async Task<SessionState> Settle(SessionReducer reducer, SessionState state, string name, string id)
    {
        state = await Fold(reducer, state, $"{{\"kind\":\"tool_dispatch\",\"tool\":{{\"id\":\"{id}\",\"name\":\"{name}\"}}}}");
        return await Fold(reducer, state, $"{{\"kind\":\"tool_result\",\"tool\":{{\"id\":\"{id}\",\"name\":\"{name}\",\"output\":\"ok\"}}}}");
    }
}
