using Reasonix.Studio.Pages;
using System.Text.Json.Nodes;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>Tests the activity projection displayed in run analysis.</summary>
public sealed class RunTimelineTests
{
    /// <summary>Matches tools and model usage by id and excludes previous turns.</summary>
    [Fact]
    public void ReplayPairsRecordsAndUsesMeasuredTime()
    {
        var frames = JsonNode.Parse("""
            [
              {"kind":"turn_started"},{"kind":"retrying"},
              {"kind":"turn_started"},
              {"kind":"stream_attempt","streamAttempt":{"id":"m","action":"begin"}},
              {"kind":"stream_attempt","streamAttempt":{"id":"m","action":"commit","firstTokenMs":60}},
              {"kind":"tool_dispatch","tool":{"id":"t","partial":true}},
              {"kind":"tool_dispatch","tool":{"id":"t","name":"read","startedAt":1200}},
              {"kind":"tool_result","tool":{"id":"t","name":"read","durationMs":820}},
              {"kind":"usage","usage":{"attemptId":"m","completionTokens":20}},
              {"kind":"turn_done","workStartedAt":1000,"workDurationMs":5000}
            ]
            """)!.AsArray();
        var analysis = RunTimeline.From(frames);
        Assert.Equal(4, analysis.Rows.Count);
        var model = Assert.Single(analysis.Rows, r => r.Bucket == "model");
        Assert.Equal(3, model.Frames.Count);
        Assert.Null(model.Duration);
        var tool = Assert.Single(analysis.Rows, r => r.Bucket == "tool");
        Assert.Equal(200, tool.Start);
        Assert.Equal(820d, tool.Duration);
        Assert.Equal(1020, analysis.Span);
        Assert.DoesNotContain(analysis.Rows, row => row.Recovery);
    }
}
