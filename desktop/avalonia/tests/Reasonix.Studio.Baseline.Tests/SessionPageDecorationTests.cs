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
            new SayItem { Id = "s2", Text = "unseen", Done = true },
        };

        var with = await Draw(new SessionState { Items = items }, seenThrough: "s1");
        var without = await Draw(new SessionState { Items = items }, seenThrough: null);

        Assert.Contains(with, text => text.Contains("从这里开始有新回答", System.StringComparison.Ordinal));
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

    /// <summary>
    /// Renders one state in a real headless window and reads the text it drew.
    /// </summary>
    /// <param name="state">The state to draw.</param>
    /// <param name="seenThrough">The last row the reader is known to have seen.</param>
    /// <param name="foldsTurnWork">Whether a turn's work folds under one line.</param>
    /// <returns>The text of every text-block the page drew, in tree order.</returns>
    private static async Task<IReadOnlyList<string>> Draw(SessionState state, string? seenThrough = null, bool foldsTurnWork = false)
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(App));

        return await session.Dispatch(
            () =>
            {
                var page = new SessionPage
                {
                    Width = 760,
                    Height = 600,
                    SeenThrough = seenThrough,
                    FoldsTurnWork = foldsTurnWork,
                };

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
