using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Wire;
using Reasonix.Studio.Pages;
using Reasonix.Studio.Shell;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the decorations to the visual tree: a group, a fold and a separator are
/// only decoration if the page actually draws them.
/// </summary>
/// <remarks>
/// The state layer's rules are held by cheap cases; these hold the drawing. A
/// page that computed a group and then drew flat rows would pass every rule case
/// and show the reader nothing different, which is the failure this exists for.
/// </remarks>
[Collection(HeadlessCollection.Name)]
public sealed class SessionPageDecorationTests
{
    /// <summary>
    /// Checks that a stretch of settled work is drawn under a head that says what
    /// it came to, with a control to fold it.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task WorkIsDrawnUnderAnActivityHead()
    {
        var texts = await Draw(new SessionState
        {
            Items = new TranscriptItem[]
            {
                new SayItem { Id = "s1", Text = "looking", Done = true },
                Call("t1"),
                Call("t2"),
                new SayItem { Id = "s2", Text = "done", Done = true },
            },
        });

        // The head is the only place two settled calls are counted, and the
        // footnote wording is what the reference frontend's table says.
        Assert.Contains(texts, text => text.Contains("2", System.StringComparison.Ordinal) && text.Contains("项操作", System.StringComparison.Ordinal));
    }

    /// <summary>
    /// Checks that two delegates sent out side by side are drawn as one group
    /// rather than as two ordinary rows.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task ParallelDelegatesAreDrawnAsOneGroup()
    {
        var texts = await Draw(new SessionState
        {
            Items = new TranscriptItem[]
            {
                new SayItem { Id = "s1", Text = "splitting the work", Done = true },
                Delegate("a"),
                Delegate("b"),
            },
        });

        Assert.Contains(texts, text => text.Contains("子代理", System.StringComparison.Ordinal));
    }

    /// <summary>
    /// Checks that the unread line is drawn before the first answer the reader has
    /// not seen, and nowhere when nothing is known.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task TheUnreadLineIsDrawnOnlyWhereItBelongs()
    {
        var items = new TranscriptItem[]
        {
            new SayItem { Id = "s1", Text = "seen", Done = true },
            new UserItem { Id = "u2", Text = "latest question", At = 10 },
            new SayItem { Id = "s2", Text = "unseen", Done = true },
        };

        var with = await Draw(new SessionState { Items = items }, viewedAt: 20);
        var without = await Draw(new SessionState { Items = items }, viewedAt: null);

        var rendered = with.ToList();
        Assert.Contains("latest question", rendered);
        Assert.True(rendered.IndexOf("latest question") < rendered.IndexOf("从这里开始有新回答"));
        Assert.True(rendered.IndexOf("从这里开始有新回答") < rendered.IndexOf("unseen"));
        Assert.DoesNotContain(without, text => text.Contains("从这里开始有新回答", System.StringComparison.Ordinal));
    }

    /// <summary>
    /// Checks that a turn's intermediate work folds under one line when the
    /// setting asks for it, and the answer stays visible.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task ATurnFoldsItsWorkAndKeepsItsAnswer()
    {
        var state = new SessionState
        {
            Items = new TranscriptItem[]
            {
                new UserItem { Id = "u1", Text = "question" },
                Call("t1"),
                new SayItem { Id = "s1", Text = "the answer", Done = true },
            },
        };

        var texts = await Draw(state, foldsTurnWork: true);

        Assert.Contains(texts, text => text.Contains("中间过程", System.StringComparison.Ordinal));
        Assert.Contains(texts, text => text.Contains("the answer", System.StringComparison.Ordinal));
    }

    /// <summary>Only viewport rows are created, and Find can realize the last row by identity.</summary>
    /// <returns>The rendered interaction.</returns>
    [Fact]
    public async Task ALongTranscriptRealizesOnlyTheViewport()
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(
            () =>
            {
                var page = new SessionPage();
                var window = new Window { Width = 800, Height = 640, Content = page };
                window.Show();
                try
                {
                    var items = Enumerable.Range(0, 5000)
                        .Select(i => (TranscriptItem)new UserItem { Id = $"row-{i}", Text = $"message {i}" })
                        .ToArray();
                    page.Show(new SessionState { Items = items });
                    Dispatcher.UIThread.RunJobs();
                    Assert.InRange(page.GetVisualDescendants().OfType<UserMessage>().Count(), 1, 120);

                    var viewport = page.GetVisualDescendants().OfType<ScrollViewer>().First();
                    foreach (var match in new[] { "message 4999", "message 2500", "message 0" })
                    {
                        Assert.Equal((1, 1), page.FindMatch(match, 0));
                        Dispatcher.UIThread.RunJobs();

                        var text = Assert.Single(page.GetVisualDescendants().OfType<SelectableTextBlock>(), block => block.Text == match);
                        var position = text.TranslatePoint(default, viewport);
                        Assert.NotNull(position);
                        Assert.InRange(position.Value.Y, 0, viewport.Viewport.Height - text.Bounds.Height);
                        Assert.Equal(0, text.SelectionStart);
                        Assert.Equal(match.Length, text.SelectionEnd);
                        Assert.InRange(page.GetVisualDescendants().OfType<UserMessage>().Count(), 1, 120);
                    }
                }
                finally
                {
                    window.Close();
                }
            },
            default);
    }

    /// <summary>A large folded activity does not realize its children and can still reveal a remote match.</summary>
    /// <returns>The rendered interaction.</returns>
    [Fact]
    public async Task ALongActivityRealizesItsMembersIndependently()
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(
            () =>
            {
                var page = new SessionPage();
                var window = new Window { Width = 800, Height = 640, Content = page };
                window.Show();
                try
                {
                    var rows = Enumerable.Range(0, 1000)
                        .Select(i => (TranscriptItem)new ToolItem
                        {
                            Id = $"tool-{i}",
                            Call = new Tool { ID = $"call-{i}", Name = $"fixture-{i}" },
                        })
                        .ToArray();
                    page.Show(new SessionState { Items = rows });
                    Dispatcher.UIThread.RunJobs();
                    Assert.Empty(page.GetVisualDescendants().OfType<ToolCallHeader>());

                    Assert.Equal((1, 1), page.FindMatch("fixture-999", 0));
                    Dispatcher.UIThread.RunJobs();
                    Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "fixture-999");
                    Assert.InRange(page.GetVisualDescendants().OfType<ToolCallHeader>().Count(), 1, 120);
                }
                finally
                {
                    window.Close();
                }
            },
            default);
    }

    /// <summary>
    /// Renders one state in a real headless window and reads the text it drew.
    /// </summary>
    /// <param name="state">The state to draw.</param>
    /// <param name="viewedAt">The host cutoff captured when the session was unread.</param>
    /// <param name="foldsTurnWork">Whether a turn's work folds under one line.</param>
    /// <returns>The text of every text-block the page drew, in tree order.</returns>
    private static async Task<IReadOnlyList<string>> Draw(SessionState state, long? viewedAt = null, bool foldsTurnWork = false)
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(App));

        return await session.Dispatch(
            () =>
            {
                var page = new SessionPage
                {
                    Width = 760,
                    Height = 600,
                    FoldsTurnWork = foldsTurnWork,
                };

                page.AnswerBoundary.Remember(viewedAt);
                var window = new Window { Width = 800, Height = 640, Content = page };
                window.Show();
                page.Show(state);

                Dispatcher.UIThread.RunJobs();

                var texts = page
                    .GetSelfAndVisualDescendants()
                    .OfType<TextBlock>()
                    .Select(block => block.Text ?? string.Empty)
                    .ToList();

                window.Close();
                return (IReadOnlyList<string>)texts;
            },
            default);
    }

    /// <summary>
    /// Builds one settled call row.
    /// </summary>
    /// <param name="id">The call's identity.</param>
    /// <returns>The row.</returns>
    private static ToolItem Call(string id)
    {
        return new ToolItem
        {
            Id = id,
            Call = new Tool { ID = id, Name = "read_file", ReadOnly = true },
            Running = false,
        };
    }

    /// <summary>
    /// Builds one delegate row, which carries a profile so it reads as one.
    /// </summary>
    /// <param name="id">The delegate's identity.</param>
    /// <returns>The row.</returns>
    private static ToolItem Delegate(string id)
    {
        return new ToolItem
        {
            Id = id,
            Call = new Tool
            {
                ID = id,
                Name = "task",
                ReadOnly = false,
                Profile = new Profile { Name = "reader" },
            },
            Running = false,
        };
    }
}
