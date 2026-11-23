using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Shell.Workbench;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks changed-file headings, sticky scrolling and the inline discard confirmation.</summary>
    /// <param name="width">Window width.</param><param name="dark">Whether the dark theme is selected.</param>
    /// <returns>The changes-board presentation and actions.</returns>
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(760, false)]
    [InlineData(760, true)]
    public Task ChangesBoardMatchesFileHeaderStates(int width, bool dark) => WithSettings("workbench", (w, reads) =>
    {
        var height = width == 760 ? 480 : 900;
        w.Width = width;
        w.Height = height;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var patch = "--- a/internal/provider/retry.go\n+++ b/internal/provider/retry.go\n@@ -1,41 +1,41 @@ Change header\n-before\n+after\n"
            + string.Concat(Enumerable.Range(1, 40).Select(i => $" context {i}\n"));
        var discards = 0;
        var count = -1;
        reads.Exchange = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri?.AbsolutePath switch
        {
            "/changes/diff" => JsonReply(new JsonObject { ["diff"] = patch }),
            "/changes/discard" => Discard(),
            "/changes/stage" => JsonReply(new JsonObject { ["ok"] = true }),
            "/changes" when count >= 0 => JsonReply(new JsonObject
            {
                ["repo"] = true,
                ["changes"] = new JsonArray(Enumerable.Range(0, count).Select(i => new JsonObject
                {
                    ["path"] = $"pkg{i}/dir{i % 9}/file{i}.go",
                    ["status"] = "??",
                    ["unstaged"] = true,
                }).ToArray()),
            }),
            _ => null,
        });

        ClickAt(w, "action:workbench.changes-page");
        var canvas = w.FindControl<ScrollViewer>("RenderedFile")!;
        var viewport = Assert.Single(canvas.GetVisualDescendants().OfType<ScrollViewer>(), item => AutomationProperties.GetAutomationId(item) == "class:wb-changes-body");
        Assert.Contains(canvas.GetVisualDescendants().OfType<TextBlock>(), text => text.Name == "BranchName" && text.Text == "main");
        var total = Assert.Single(canvas.GetVisualDescendants().OfType<TextBlock>(), text => text.Name == "ChangeTotal");
        Assert.Contains(total.Inlines!.OfType<Run>(), run => run.Text == "+20");
        Assert.Contains(total.Inlines!.OfType<Run>(), run => run.Text == "−5");
        var head = viewport.GetVisualDescendants().OfType<Border>().First(border => border.Name == "FileHeader");
        var stage = Action(head, "workbench.stage");
        var fold = Action(head, "workbench.change-fold");
        var discard = Action(head, "workbench.discard");
        var open = Action(head, "workbench.change-open");
        Assert.Equal(36, head.Bounds.Height);
        Assert.Equal(10, fold.TranslatePoint(default, head)!.Value.X);
        Assert.Equal(36, stage.TranslatePoint(default, head)!.Value.X);
        Assert.Equal(14, stage.Bounds.Width);
        Assert.False(open.IsEffectivelyVisible);
        Assert.False(discard.IsEffectivelyVisible);
        var tabCount = Assert.Single(w.GetVisualDescendants().OfType<TextBlock>(), text => text.Name == "TabCount" && text.IsEffectivelyVisible && text.Text == "4");
        var tab = Assert.Single(tabCount.GetVisualAncestors().OfType<WorkbenchHeaderTab>());
        Assert.NotNull(tab.FindControl<Avalonia.Controls.Shapes.Path>("TabGlyph")!.Data);
        Capture("normal");
        var stageAll = Action(canvas, "workbench.stage-all");
        var commit = Action(canvas, "commit.draft");
        CaptureToolbarHover(stageAll, "stage-hover");
        CaptureToolbarHover(commit, "commit-hover");

        w.MouseMove(head.TranslatePoint(new Point(110, 18), w)!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.True(open.IsEffectivelyVisible);
        Assert.Equal("打开文件", open.Content);
        Assert.True(discard.IsEffectivelyVisible);
        Assert.Equal("丢弃", discard.Content);
        Capture("hover");

        MovePointerTo(discard);
        Assert.True(discard.Focus(NavigationMethod.Pointer));
        Click(discard);
        Assert.Equal("确认丢弃", discard.Content);
        Assert.Equal(0, discards);
        Capture("confirm");
        Assert.True(fold.Focus(NavigationMethod.Tab));
        Assert.Equal("丢弃", discard.Content);

        Click(fold);
        w.MouseMove(default);
        Assert.True(stageAll.Focus(NavigationMethod.Pointer));
        Capture("folded");
        Assert.Equal("›", fold.Content);
        Click(fold);
        w.UpdateLayout();
        var before = head.TranslatePoint(default, viewport);
        viewport.Offset = new Vector(0, 135);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(135, viewport.Offset.Y);
        Assert.Equal(before, head.TranslatePoint(default, viewport));
        Capture("sticky");
        viewport.Offset = new Vector(0, viewport.Extent.Height);
        Dispatcher.UIThread.RunJobs();
        Assert.True(head.TranslatePoint(default, viewport)!.Value.Y < 0);
        var peek = viewport.GetVisualDescendants().OfType<Button>().Last(button => AutomationProperties.GetAutomationId(button) == "action:workbench.change-peek" && button.IsEffectivelyVisible);
        var peekLabel = Assert.Single(peek.GetVisualDescendants().OfType<TextBlock>());
        var faint = dark ? Color.Parse("#8e9289") : Color.Parse("#666a63");
        Assert.Equal(faint, Assert.IsAssignableFrom<ISolidColorBrush>(peekLabel.Foreground).Color);
        Capture("peek");

        viewport.Offset = default;
        Dispatcher.UIThread.RunJobs();
        w.MouseMove(head.TranslatePoint(new Point(110, 18), w)!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.True(discard.Focus(NavigationMethod.Tab));
        Click(discard);
        Assert.Equal(0, discards);
        count = 9;
        Click(discard);
        Assert.Equal(1, discards);
        Assert.Contains(viewport.GetVisualDescendants().OfType<TextBlock>(), item => item.Name == "FileTag" && item.IsEffectivelyVisible && item.Text == "未跟踪");
        Assert.Contains(canvas.GetVisualDescendants().OfType<TextBlock>(), item => item.Name == "ChangeTotal" && item.Inlines!.OfType<Run>().Any(run => run.Text!.Contains("未跟踪 9", System.StringComparison.Ordinal)));
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), text => text.Name == "TabCount" && text.IsEffectivelyVisible && text.Text == "9");
        Capture("many");

        count = 0;
        Click(Action(canvas, "workbench.stage-all"));
        Assert.Contains(canvas.GetVisualDescendants().OfType<TextBlock>(), item => item.Name == "NoChanges" && item.IsEffectivelyVisible);
        Assert.False(Action(canvas, "workbench.stage-all").IsEnabled);
        Assert.DoesNotContain(w.GetVisualDescendants().OfType<TextBlock>(), text => text.Name == "TabCount" && text.IsEffectivelyVisible);
        Capture("empty");

        static Button Action(Control scope, string id) => scope.GetVisualDescendants().OfType<Button>()
            .First(button => AutomationProperties.GetAutomationId(button) == "action:" + id);

        static void Click(Button button)
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        void MovePointerTo(Control control)
        {
            w.MouseMove(control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), w)!.Value);
            Dispatcher.UIThread.RunJobs();
        }

        void CaptureToolbarHover(Button button, string state)
        {
            var background = button.Background;
            MovePointerTo(button);
            Assert.True(button.IsPointerOver);
            Assert.Same(background, button.Background);
            Capture(state);
        }

        void Capture(string state) => CaptureFlow(w, $"native-changes-{state}-{width}x{height}", resetScroll: false);

        HttpResponseMessage Discard()
        {
            discards++;
            return JsonReply(new JsonObject { ["ok"] = true });
        }
    });
}
