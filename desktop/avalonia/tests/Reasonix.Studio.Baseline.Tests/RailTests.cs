using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the sidebar's order and its row set: what a reader scans, and what stays
/// behind the row that owns it.
/// </summary>
/// <remarks>
/// The order is the whole rule: pinned first, then newest-first, archived last.
/// Copies and versions never become rows, because one conversation with a dozen
/// conflicting saves would otherwise fill the column with files nobody made.
/// </remarks>
public sealed class RailTests
{
    /// <summary>
    /// Checks that a project opens with its own row and one row per conversation.
    /// </summary>
    [Fact]
    public void AProjectListsItsConversationsBeneathIt()
    {
        var rows = RailPresenter.Rows(Tree(Project("C:\\work", Session("a.jsonl", 10), Session("b.jsonl", 20))), null, InterfaceLanguage.Zh);

        Assert.Equal(3, rows.Count);
        Assert.Equal(RailRowKind.Project, rows[0].Kind);
        Assert.Equal("work", rows[0].Title);
        Assert.All(rows.Skip(1), row => Assert.Equal(RailRowKind.Session, row.Kind));
        Assert.All(rows.Skip(1), row => Assert.Equal(1, row.Indent));
    }

    /// <summary>
    /// Checks that pinning is a stable partition of the server-ordered tree.
    /// </summary>
    [Fact]
    public void PinningPreservesTheManualOrderOfOtherSessions()
    {
        var rows = RailPresenter.Rows(
            Tree(Project(
                "C:\\work",
                Session("old.jsonl", 10),
                Session("archived.jsonl", 90, archived: true),
                Session("pinned-old.jsonl", 5, pinned: true),
                Session("new.jsonl", 50))),
            null,
            InterfaceLanguage.Zh);

        var order = rows
            .Where(row => row.Kind == RailRowKind.Session)
            .Select(row => row.Title)
            .ToList();

        Assert.Equal(
            new[] { "pinned-old.jsonl", "old.jsonl", "archived.jsonl", "new.jsonl" },
            order);
    }

    /// <summary>
    /// Checks that a missing timestamp does not move a manually ordered row.
    /// </summary>
    [Fact]
    public void ManualOrderSurvivesMissingTimestamps()
    {
        var rows = RailPresenter.Rows(
            Tree(Project("C:\\work", Session("unknown.jsonl", null), Session("known.jsonl", 1))),
            null,
            InterfaceLanguage.Zh);

        Assert.Equal(
            new[] { "unknown.jsonl", "known.jsonl" },
            rows.Where(row => row.Kind == RailRowKind.Session)
                .Select(row => row.Title));
    }

    /// <summary>
    /// Checks that copies and versions never become rows of their own, and that
    /// the row they belong to says how many stand behind it.
    /// </summary>
    [Fact]
    public void CopiesAndVersionsStayBehindTheirRow()
    {
        var session = Session("main.jsonl", 20) with
        {
            Copies = new[] { Session("main copy.jsonl", 19) },
            Versions = new[] { Session("main v1.jsonl", 18), Session("main v2.jsonl", 17) },
        };

        var rows = RailPresenter.Rows(Tree(Project("C:\\work", session)), null, InterfaceLanguage.Zh);

        Assert.Equal(2, rows.Count);
        Assert.Equal(3, rows[1].Collapsed);
        Assert.DoesNotContain(rows, row => row.Path.Contains("copy", StringComparison.Ordinal));
        Assert.DoesNotContain(rows, row => row.Path.Contains("v1", StringComparison.Ordinal));
    }

    /// <summary>
    /// Checks that the marks a reader scans for are read off the row: unread, pin,
    /// running, archived and legacy.
    /// </summary>
    [Fact]
    public void TheRowCarriesTheMarksItsStateEarns()
    {
        var session = Session("a.jsonl", 10) with
        {
            Unread = true,
            Pinned = true,
            Archived = true,
            Legacy = true,
            RuntimeId = "pane-1",
        };

        var row = RailPresenter.Rows(Tree(Project("C:\\work", session)), null, InterfaceLanguage.Zh)[1];

        Assert.True(row.Unread);
        Assert.True(row.Pinned);
        Assert.True(row.Archived);
        Assert.True(row.Running);
    }

    /// <summary>
    /// Checks that the conversation this window has open is marked running even
    /// when the kernel has not named a pane for it yet.
    /// </summary>
    [Fact]
    public void TheOpenConversationIsTheRunningOne()
    {
        var rows = RailPresenter.Rows(
            Tree(Project("C:\\work", Session("a.jsonl", 10), Session("b.jsonl", 20))),
            @"C:\work\a.jsonl",
            InterfaceLanguage.Zh);

        Assert.True(rows.Single(row => row.Path.EndsWith("a.jsonl", StringComparison.Ordinal)).Running);
        Assert.False(rows.Single(row => row.Path.EndsWith("b.jsonl", StringComparison.Ordinal)).Running);

        // Paths are compared case-blind: Windows hands back the same file in two
        // spellings depending on who asked.
        Assert.True(RailPresenter.Same(@"C:\WORK\A.JSONL", @"c:\work\a.jsonl"));
        Assert.False(RailPresenter.Same(string.Empty, string.Empty));
    }

    /// <summary>
    /// Checks that a project's title subtitle is its count, and that a folder that
    /// is gone says so instead.
    /// </summary>
    [Fact]
    public void AProjectSaysItsCountOrThatItIsGone()
    {
        var rows = RailPresenter.Rows(
            new[]
            {
                Project("C:\\work", Session("a.jsonl", 1), Session("b.jsonl", 2)),
                Project("C:\\gone", Session("c.jsonl", 3)) with { Missing = true },
            },
            null,
            InterfaceLanguage.Zh);

        Assert.Equal("2", rows[0].Subtitle);
        Assert.Equal("文件夹已不在磁盘上", rows.Where(row => row.Kind == RailRowKind.Project).Last().Subtitle);
    }

    /// <summary>
    /// Checks that a conversation with no counted turns says nothing rather than
    /// "0 turns", which reads as a fact instead of an absence.
    /// </summary>
    [Fact]
    public void AnUncountedConversationSaysNothingAboutTurns()
    {
        var uncounted = Session("a.jsonl", 1);
        var counted = uncounted with
        {
            Turns = 12,
        };

        Assert.Equal(string.Empty, RailPresenter.SessionSubtitle(uncounted, InterfaceLanguage.Zh));
        Assert.Equal("12 轮对话", RailPresenter.SessionSubtitle(counted, InterfaceLanguage.Zh));
    }

    /// <summary>
    /// Checks that a project with an unread conversation shows its own dot, so a
    /// folded project does not hide the news inside it.
    /// </summary>
    [Fact]
    public void AFoldedProjectStillShowsUnreadNews()
    {
        var unread = Session("a.jsonl", 1) with
        {
            Unread = true,
        };
        var rows = RailPresenter.Rows(
            Tree(Project("C:\\work", unread, Session("b.jsonl", 2))),
            null,
            InterfaceLanguage.Zh);

        Assert.True(rows[0].Unread);
    }

    /// <summary>
    /// Builds one project.
    /// </summary>
    /// <param name="root">The folder.</param>
    /// <param name="sessions">The conversations under it.</param>
    /// <returns>The project.</returns>
    private static TreeWorkspace Project(string root, params TreeSession[] sessions)
    {
        return new TreeWorkspace
        {
            Root = root,
            Name = root.Substring(root.LastIndexOf('\\') + 1),
            Remembered = true,
            Total = sessions.Length,
            Sessions = sessions,
        };
    }

    /// <summary>
    /// Builds one conversation.
    /// </summary>
    /// <param name="name">The transcript's file name.</param>
    /// <param name="lastActiveAt">When it was last written, or null when unknown.</param>
    /// <param name="pinned">Whether it is pinned.</param>
    /// <param name="archived">Whether it is archived.</param>
    /// <returns>The conversation.</returns>
    private static TreeSession Session(string name, long? lastActiveAt, bool pinned = false, bool archived = false)
    {
        return new TreeSession
        {
            Path = @"C:\work\" + name,
            Name = name,
            LastActiveAt = lastActiveAt,
            Pinned = pinned,
            Archived = archived,
        };
    }

    /// <summary>
    /// Wraps projects into the list a read answers with.
    /// </summary>
    /// <param name="projects">The projects.</param>
    /// <returns>The tree.</returns>
    private static IReadOnlyList<TreeWorkspace> Tree(params TreeWorkspace[] projects)
    {
        return projects;
    }
}
