using Reasonix.Studio.Contracts.Events;
using Reasonix.Studio.State;
using System;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>Holds streamed-rate and measured-round accounting to their distinct sources.</summary>
public sealed class GenerationTelemetryTests
{
    /// <summary>Counts text, reasoning and only newly arrived argument characters.</summary>
    [Fact]
    public void RatesUseActualArrivalsAndExpireWithoutInventingIntermediateValues()
    {
        long now = 1000;
        var meter = new GenerationTelemetry(() => now);
        void Observe(string json)
        {
            Assert.True(WireFrame.TryParse(json, out var frame));
            meter.Observe(frame);
        }

        Observe("{\"kind\":\"turn_started\"}");
        Observe("{\"kind\":\"text\",\"text\":\"abcdefgh\"}");
        now = 2000;
        Observe("{\"kind\":\"reasoning\",\"text\":\"中文\"}");
        Assert.Equal(4, meter.Read(true).CurrentRate);
        now = 2300;
        Assert.Equal(4, meter.Read(true).CurrentRate);
        Observe("{\"kind\":\"tool_dispatch\",\"tool\":{\"id\":\"a\",\"partial\":true,\"argChars\":8}}");
        var rate = meter.Read(true).CurrentRate;
        Observe("{\"kind\":\"tool_dispatch\",\"tool\":{\"id\":\"a\",\"partial\":true,\"argChars\":8}}");
        Assert.Equal(rate, meter.Read(true).CurrentRate);
        now = 6400;
        Assert.Equal(0, meter.Read(true).CurrentRate);
        Assert.Equal(0, meter.Read(false).CurrentRate);
    }

    /// <summary>Replay carries output and host timings but cannot manufacture throughput.</summary>
    [Fact]
    public void ReplayHasNoArrivalSamplesOrSyntheticAverage()
    {
        var meter = new GenerationTelemetry(() => 1000);
        void Replay(string json)
        {
            Assert.True(WireFrame.TryParse(json, out var frame));
            meter.Observe(frame with
            {
                IsReplay = true,
            });
        }

        Replay("{\"kind\":\"turn_started\"}");
        Replay("{\"kind\":\"stream_attempt\",\"streamAttempt\":{\"id\":\"a\",\"action\":\"begin\"}}");
        Replay("{\"kind\":\"text\",\"text\":\"previous output\"}");
        Replay("{\"kind\":\"stream_attempt\",\"streamAttempt\":{\"id\":\"a\",\"action\":\"commit\",\"firstTokenMs\":1250}}");
        Replay("{\"kind\":\"usage\",\"usage\":{\"attemptId\":\"a\",\"completionTokens\":80}}");
        var reading = meter.Read(false);
        Assert.Equal(80, reading.OutputTokens);
        Assert.Equal(1.25, reading.FirstTokenSeconds);
        Assert.Equal(0, reading.AverageRate);
        Assert.Empty(reading.Trail);
    }

    /// <summary>The measured attempt interval, not total turn time, defines average speed.</summary>
    [Fact]
    public void AverageUsesOnlyTimedModelRoundsAndTrailKeepsSixtySamples()
    {
        long now = 1000;
        var meter = new GenerationTelemetry(() => now);
        void Observe(string json)
        {
            Assert.True(WireFrame.TryParse(json, out var frame));
            meter.Observe(frame);
        }

        Observe("{\"kind\":\"turn_started\"}");
        Observe("{\"kind\":\"stream_attempt\",\"streamAttempt\":{\"id\":\"a\",\"action\":\"begin\"}}");
        now = 3000;
        Observe("{\"kind\":\"stream_attempt\",\"streamAttempt\":{\"id\":\"a\",\"action\":\"commit\",\"firstTokenMs\":640}}");
        Observe("{\"kind\":\"usage\",\"usage\":{\"attemptId\":\"a\",\"completionTokens\":80}}");
        Assert.Equal(40, meter.Read(true).AverageRate);
        Assert.Equal(2, meter.Read(true).ModelSeconds);
        for (var n = 0; n < 65; n++)
        {
            now += 1000;
            meter.Read(true);
        }

        Assert.Equal(60, meter.Read(true).Trail.Count);
        Assert.Equal(60, meter.Read(false).Trail.Count);
    }
}
