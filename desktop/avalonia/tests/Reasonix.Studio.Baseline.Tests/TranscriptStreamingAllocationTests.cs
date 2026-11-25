using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>Checks the allocation cost and snapshot isolation of streamed text.</summary>
public sealed class TranscriptStreamingAllocationTests
{
    /// <summary>Growing an answer reads its held history and prefix without copying either on each delta.</summary>
    [Fact]
    public void DeltasShareTheLoadedHistoryAndAnswerPrefix()
    {
        var appender = new TextAppender(new IdSequence());
        IReadOnlyList<TranscriptItem> history = Enumerable.Repeat<TranscriptItem>(new UserItem { Id = "user", Text = "question" }, 5000).ToArray();
        var held = appender.Append(history, new string('a', 20_000), reasoning: false);
        var next = appender.Append(held, "b", reasoning: false);
        var chunk = new string('c', 64);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 128; i++)
        {
            next = appender.Append(next, chunk, reasoning: false);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(allocated, 0, 512_000);
        Assert.Equal(20_000, Assert.IsType<SayItem>(held[^1]).Text.Length);
        var settled = Assert.IsType<SayItem>(appender.Seal(next, all: true)[^1]);
        Assert.True(settled.Done);
        Assert.Equal(20_000 + 1 + (128 * 64), settled.Text.Length);
        Assert.EndsWith(chunk, settled.Text, StringComparison.Ordinal);
    }
}
