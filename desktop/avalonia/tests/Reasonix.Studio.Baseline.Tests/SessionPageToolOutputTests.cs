using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Wire;
using Reasonix.Studio.Pages;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>
/// Holds the three steps a card takes when its output was longer than the page
/// keeps: folded to its name, opened to its two ends with the count of what is
/// missing, and read back whole from the kernel on request.
/// </summary>
/// <remarks>
/// The cap and the shape readers are held by cheap cases. These hold the card: a
/// page that capped the text and then drew all of it anyway would pass every one
/// of those and still put ten megabytes in the visual tree.
/// </remarks>
[Collection(HeadlessCollection.Name)]
public sealed class SessionPageToolOutputTests
{
    /// <summary>
    /// Checks that a call whose middle was dropped starts folded to its name and
    /// says how much is missing once opened.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task ALongCallsMiddleIsCountedAndItsOutputStaysClosed()
    {
        var (texts, visible) = await Draw(Capped());

        // The row's own name is drawn; the kept ends are built but not shown.
        Assert.Contains("bash", texts);
        Assert.Contains(texts, text => text.Contains("没有放在页面里", System.StringComparison.Ordinal));
        Assert.DoesNotContain(visible, text => text.Contains("head-", System.StringComparison.Ordinal));
    }

    /// <summary>
    /// Checks that a call which kept its whole output draws the one line it
    /// always did, with no note and no button.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task AShortCallDrawsOneLine()
    {
        var (texts, _) = await Draw(new ToolItem
        {
            Id = "c1",
            Call = new Tool { ID = "c1", Name = "bash", Output = "ok", ReadOnly = false },
        });

        Assert.Contains(MessageCatalog.Get("chat.run_command", InterfaceLanguage.Zh), texts);
        Assert.DoesNotContain(texts, text => text.Contains("没有放在页面里", System.StringComparison.Ordinal));
        Assert.DoesNotContain(texts, text => text.Contains("显示完整输出", System.StringComparison.Ordinal));
    }

    /// <summary>
    /// Checks that a call the page kept whole because another reader counts it
    /// draws one line too, even when its output is long.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task ACallKeptWholeDrawsOneLine()
    {
        var row = new ToolItem
        {
            Id = "c1",
            Call = new Tool
            {
                ID = "c1",
                Name = "grep",
                Output = new string('x', 40_000),
                ReadOnly = true,
            },
        };

        var (texts, _) = await Draw(row);

        Assert.Contains(MessageCatalog.Get("chat.search_code", InterfaceLanguage.Zh), texts);
        Assert.DoesNotContain(texts, text => text.Contains("没有放在页面里", System.StringComparison.Ordinal));
    }

    /// <summary>
    /// Renders one call and reads the text the page built, split by whether it is
    /// on screen.
    /// </summary>
    /// <param name="row">The call row.</param>
    /// <returns>Every text block's text, and the subset that is visible.</returns>
    private static async Task<(IReadOnlyList<string> Texts, IReadOnlyList<string> Visible)> Draw(ToolItem row)
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(App));

        return await session.Dispatch(
            () =>
            {
                var page = new SessionPage { Width = 760, Height = 600 };
                var window = new Window { Width = 800, Height = 640, Content = page };
                window.Show();
                page.Show(new SessionState { Items = new TranscriptItem[] { row } });

                Dispatcher.UIThread.RunJobs();
                page.GetVisualDescendants().OfType<Button>().First(button => button.Classes.Contains("activity-toggle")).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                if (row.Hidden <= 0)
                {
                    Assert.Contains(page.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetName(button) == row.Call.Name);
                }

                var blocks = page.GetSelfAndVisualDescendants().OfType<TextBlock>().ToList();
                var texts = blocks.Select(block => block.Text ?? string.Empty).ToList();
                var visible = blocks.Where(block => block.IsVisible).Select(block => block.Text ?? string.Empty).ToList();

                window.Close();
                return ((IReadOnlyList<string>)texts, (IReadOnlyList<string>)visible);
            },
            default);
    }

    /// <summary>
    /// Builds a call whose output the page capped, as settling one would.
    /// </summary>
    /// <returns>The row.</returns>
    private static ToolItem Capped()
    {
        var edge = ToolOutputCap.Edge;
        var full = "head-" + new string('x', edge - 5) + new string('M', 30_000) + new string('y', edge - 5) + "-tail";
        var capped = ToolOutputCap.Apply(new Tool { ID = "c1", Name = "bash", Output = full, ReadOnly = false });

        return new ToolItem
        {
            Id = "c1",
            Call = capped.Output,
            Running = false,
            Hidden = capped.Hidden,
        };
    }
}
