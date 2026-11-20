using Reasonix.Studio.Contracts.Wire;
using Reasonix.Studio.State.Transcript;
using System.Linq;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the bound on what a page keeps of a finished call's output.
/// </summary>
/// <remarks>
/// A call that printed ten megabytes is one the transcript still has to draw. The
/// bound keeps its two ends, counts what it dropped, and leaves the rest in the
/// kernel to be read back a page at a time — but it must never cut a shape another
/// reader counts, and it must never split a character in half.
/// </remarks>
public sealed class ToolOutputCapTests
{
    /// <summary>
    /// Checks that a short output is kept as it is, with nothing counted as lost.
    /// </summary>
    [Fact]
    public void AShortOutputIsKeptWhole()
    {
        var output = new string('x', ToolOutputCap.Ceiling);
        var capped = ToolOutputCap.Apply(Call("bash", output));

        Assert.Equal(output, capped.Output.Output);
        Assert.Equal(0, capped.Hidden);
    }

    /// <summary>
    /// Checks that a long output keeps its first and last four kilobytes and
    /// counts the middle it let go of.
    /// </summary>
    [Fact]
    public void ALongOutputKeepsItsTwoEndsAndCountsTheMiddle()
    {
        var edge = ToolOutputCap.Edge;
        var full = "head-" + new string('x', edge - 5) + new string('M', 30_000) + new string('y', edge - 5) + "-tail";

        var capped = ToolOutputCap.Apply(Call("bash", full));

        Assert.Equal(30_000, capped.Hidden);
        Assert.StartsWith("head-", capped.Output.Output, System.StringComparison.Ordinal);
        Assert.EndsWith("-tail", capped.Output.Output, System.StringComparison.Ordinal);
        Assert.Contains(ToolOutputCap.Ellipsis, capped.Output.Output, System.StringComparison.Ordinal);
        Assert.DoesNotContain(new string('M', 100), capped.Output.Output, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks that a cut never leaves half of a surrogate pair at either end.
    /// </summary>
    [Fact]
    public void ACutNeverSplitsACharacter()
    {
        // One astral character straddles the edge: two UTF-16 units, one glyph.
        var edge = ToolOutputCap.Edge;
        var full = new string('a', edge - 1) + "😀" + new string('b', 30_000) + new string('c', edge);

        var capped = ToolOutputCap.Apply(Call("bash", full));

        Assert.DoesNotContain('\uFFFD', capped.Output.Output);
        Assert.True(char.IsHighSurrogate(capped.Output.Output[edge - 1]) == false, "the kept head ends on half a character");
    }

    /// <summary>
    /// Checks that the calls whose output another reader counts are never cut: a
    /// plan, a remembered fact, a diff, and every lookup.
    /// </summary>
    [Fact]
    public void AShapeAnotherReaderCountsIsKeptWhole()
    {
        var long_ = new string('z', 40_000);

        foreach (var name in new[] { "todo_write", "remember", "update_goal", "read_file", "grep", "glob", "ls" })
        {
            var capped = ToolOutputCap.Apply(Call(name, long_));

            Assert.Equal(long_, capped.Output.Output);
            Assert.Equal(0, capped.Hidden);
        }
    }

    /// <summary>
    /// Checks that an output the kernel already marked as a diff is kept whole,
    /// because the diff card draws all of it.
    /// </summary>
    [Fact]
    public void ADiffIsKeptWhole()
    {
        var long_ = new string('d', 40_000);

        var marked = Call("bash", long_) with
        {
            OutputDiff = true,
        };
        var carried = Call("bash", long_) with
        {
            Diff = "--- a\n+++ b",
        };

        Assert.Equal(0, ToolOutputCap.Apply(marked).Hidden);
        Assert.Equal(0, ToolOutputCap.Apply(carried).Hidden);
    }

    /// <summary>
    /// Builds a finished call.
    /// </summary>
    /// <param name="name">The tool's name.</param>
    /// <param name="output">What it printed.</param>
    /// <returns>The call.</returns>
    private static Tool Call(string name, string output)
    {
        return new Tool { ID = "c1", Name = name, Output = output, ReadOnly = false };
    }
}
