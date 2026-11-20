using Reasonix.Studio.Contracts.Tree;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the sidebar's tree to the shape the kernel sends, including the parts a
/// flattened model would lose.
/// </summary>
/// <remarks>
/// A conversation is not one row: conflicting saves leave copies, edits leave
/// versions, and both are whole conversations that open like any other. A model
/// that read only the top level would draw a sidebar missing half the work, so
/// the cases here start from the kernel's own JSON rather than from objects built
/// to match the C# side.
/// </remarks>
public sealed class TreeTests
{
    /// <summary>
    /// Checks that a conversation's copies and versions are read as whole
    /// conversations, recursively, so a reader can open any of them.
    /// </summary>
    [Fact]
    public void CopiesAndVersionsAreWholeConversations()
    {
        const string Json = """
        [
          {
            "root": "C:\\work",
            "name": "work",
            "remembered": true,
            "total": 3,
            "remaining": 0,
            "sessions": [
              {
                "path": "C:\\work\\.reasonix\\main.jsonl",
                "name": "main",
                "title": "the conversation",
                "turns": 12,
                "pinned": true,
                "unread": true,
                "viewedAt": 1700000000000,
                "copies": [
                  {
                    "path": "C:\\work\\.reasonix\\main copy.jsonl",
                    "name": "main copy",
                    "copies": [ { "path": "C:\\work\\.reasonix\\nested.jsonl", "name": "nested" } ]
                  }
                ],
                "versions": [
                  { "path": "C:\\work\\.reasonix\\main v1.jsonl", "name": "main v1", "versions": [] }
                ]
              }
            ]
          }
        ]
        """;

        var workspaces = JsonSerializer.Deserialize<List<TreeWorkspace>>(Json);
        var workspace = Assert.Single(workspaces!);
        var session = Assert.Single(workspace.Sessions);

        Assert.True(workspace.Remembered);
        Assert.Equal(3, workspace.Total);
        Assert.Equal(0, workspace.Remaining);

        Assert.Equal("the conversation", session.Title);
        Assert.Equal(12, session.Turns);
        Assert.True(session.Pinned);

        var copy = Assert.Single(session.Copies);
        Assert.Equal("main copy", copy.Name);

        // The nested copy is a copy of a copy, and it is still a whole row.
        Assert.Equal("nested", Assert.Single(copy.Copies).Name);

        var version = Assert.Single(session.Versions);
        Assert.Equal("main v1", version.Name);
        Assert.Empty(version.Versions);
    }

    /// <summary>
    /// Checks that a seen row carries no viewed time, so the absence reads as
    /// seen rather than as a moment nobody looked.
    /// </summary>
    [Fact]
    public void ASeenRowCarriesNoViewedTime()
    {
        const string Json = """{ "path": "a.jsonl", "name": "a" }""";

        var session = JsonSerializer.Deserialize<TreeSession>(Json)!;

        Assert.False(session.Unread);
        Assert.Null(session.ViewedAt);
    }

    /// <summary>
    /// Checks that an unknown last-write time stays unknown rather than reading as
    /// the epoch, which a recency ordering would sort as the oldest row.
    /// </summary>
    [Fact]
    public void AnUnknownLastWriteStaysUnknown()
    {
        var session = JsonSerializer.Deserialize<TreeSession>("""{ "path": "a.jsonl" }""")!;

        Assert.Null(session.LastActiveAt);
        Assert.Null(session.AutoArchivedAt);
        Assert.Null(session.CountsKnown);
    }

    /// <summary>
    /// Checks that the default window always asks for the progressive listing and
    /// nothing else.
    /// </summary>
    [Fact]
    public void TheDefaultWindowAsksForTheProgressiveListing()
    {
        Assert.Equal("?progressive=1", new TreeWindow().QueryString());
    }

    /// <summary>
    /// Checks that a recent page carries its offset and order, which is the only
    /// combination the kernel accepts an offset with.
    /// </summary>
    [Fact]
    public void ARecentPageCarriesItsOffsetAndOrder()
    {
        var query = new TreeWindow { Order = "recent", Offset = 50, Limit = 25 }.QueryString();

        Assert.Contains("order=recent", query, StringComparison.Ordinal);
        Assert.Contains("offset=50", query, StringComparison.Ordinal);
        Assert.Contains("limit=25", query, StringComparison.Ordinal);
        Assert.Contains("progressive=1", query, StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks that per-project bounds travel as one JSON object, the shape the
    /// kernel parses.
    /// </summary>
    [Fact]
    public void PerProjectBoundsTravelAsOneObject()
    {
        var query = new TreeWindow
        {
            Limits = new Dictionary<string, int> { [@"C:\work"] = 10 },
        }.QueryString();

        Assert.Contains("limits=", query, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString(@"{""C:\\work"":10}"), query, StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks that the search text is trimmed, escaped, and dropped past the
    /// kernel's limit instead of being sent for a refusal.
    /// </summary>
    [Fact]
    public void TheSearchTextIsTrimmedAndBounded()
    {
        Assert.Contains("q=a%20b", new TreeWindow { Query = "  a b  " }.QueryString(), StringComparison.Ordinal);

        var tooLong = new string('x', TreeWindow.MaxQueryBytes + 1);
        Assert.DoesNotContain("q=", new TreeWindow { Query = tooLong }.QueryString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks that the flags the reference frontend sends for a retry and a legacy
    /// filter reach the kernel as the strings it reads.
    /// </summary>
    [Fact]
    public void TheFlagsAreSentAsTheKernelReadsThem()
    {
        var query = new TreeWindow { RetryMetadata = true, HideLegacy = true, Scope = "archived" }.QueryString();

        Assert.Contains("retryMetadata=1", query, StringComparison.Ordinal);
        Assert.Contains("hideLegacy=1", query, StringComparison.Ordinal);
        Assert.Contains("scope=archived", query, StringComparison.Ordinal);
    }
}
