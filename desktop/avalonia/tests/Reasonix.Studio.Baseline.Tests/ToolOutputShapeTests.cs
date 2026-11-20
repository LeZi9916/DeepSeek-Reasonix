using Reasonix.Studio.State.Transcript;
using System.Linq;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the shapes a lookup's output is read into.
/// </summary>
/// <remarks>
/// Each shape was read off a real tool result. What matters as much as reading
/// the shape is refusing it: an output that only half fits must fall back to the
/// raw block, because a wrong list reads as authoritative while a raw block only
/// reads as raw.
/// </remarks>
public sealed class ToolOutputShapeTests
{
    /// <summary>
    /// Checks that search hits are read as path, line and text.
    /// </summary>
    [Fact]
    public void SearchHitsAreRead()
    {
        var read = ToolOutputShape.Hits("src/app.ts:12:const a = 1;\nsrc/app.ts:20:const b = 2;\n");

        Assert.NotNull(read);
        Assert.Equal(2, read!.Value.Hits.Count);
        Assert.Equal("src/app.ts", read.Value.Hits[0].Path);
        Assert.Equal("12", read.Value.Hits[0].Line);
        Assert.Equal("const a = 1;", read.Value.Hits[0].Text);
    }

    /// <summary>
    /// Checks that a context line joins the hit above it, with its gutter
    /// stripped by the hit's own path rather than by a pattern.
    /// </summary>
    /// <remarks>
    /// A path holding <c>-12-</c> would fool a pattern that read any digits
    /// between dashes; stripping the literal path first cannot.
    /// </remarks>
    [Fact]
    public void AContextLineJoinsItsHit()
    {
        var output = "dir/file-12-x.ts:7:hit\ndir/file-12-x.ts-8-context\ndir/file-12-x.ts:9:next\n";

        var read = ToolOutputShape.Hits(output);

        Assert.NotNull(read);
        Assert.Equal(2, read!.Value.Hits.Count);
        Assert.Equal(new[] { "context" }, read.Value.Hits[0].More);
    }

    /// <summary>
    /// Checks that the tool's own truncation note is pulled out and kept.
    /// </summary>
    [Fact]
    public void ATruncationNoteIsKeptApartFromTheHits()
    {
        var read = ToolOutputShape.Hits("a.ts:1:one\n... (truncated at 100 matches)\n");

        Assert.NotNull(read);
        Assert.Single(read!.Value.Hits);
        Assert.Contains("truncated", read.Value.Note, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks that an output which only half fits is refused rather than drawn as
    /// a list.
    /// </summary>
    [Fact]
    public void AnOutputThatOnlyHalfFitsIsRefused()
    {
        var output = "a.ts:1:one\nnothing here looks like a hit\nanother loose line\nand a fourth\n";

        Assert.Null(ToolOutputShape.Hits(output));
    }

    /// <summary>
    /// Checks that a listing reads files with their sizes and directories without.
    /// </summary>
    [Fact]
    public void AListingReadsFilesAndDirectories()
    {
        var rows = ToolOutputShape.Listing("src/\nREADME.md\t1024\nLICENSE\t2048\n");

        Assert.NotNull(rows);
        Assert.Equal(3, rows!.Count);
        Assert.Equal(("src/", string.Empty), (rows[0].Name, rows[0].Count));
        Assert.Equal(("README.md", "1024"), (rows[1].Name, rows[1].Count));
    }

    /// <summary>
    /// Checks that an empty output reads as no shape at all.
    /// </summary>
    [Fact]
    public void AnEmptyOutputReadsAsNoShape()
    {
        Assert.Null(ToolOutputShape.Hits("   \n\n"));
        Assert.Null(ToolOutputShape.Listing("   \n\n"));
    }

    /// <summary>
    /// Checks that the counted-field reader refuses an output it cannot account
    /// for, so a stray line is not silently dropped from a claimed list.
    /// </summary>
    [Fact]
    public void TheCountedFieldReaderRefusesWhatItCannotAccountFor()
    {
        var pattern = new System.Text.RegularExpressions.Regex(@"^(.+): (\d+) matches$");

        var rows = ToolOutputShape.Rows("a.ts: 3 matches\nb.ts: 4 matches\nc.ts: 5 matches\n", pattern);
        Assert.NotNull(rows);
        Assert.Equal(3, rows!.Count);

        Assert.Null(ToolOutputShape.Rows("a.ts: 3 matches\nb.ts: 4 matches\nsomething else entirely\nmore prose\n", pattern));
    }
}
