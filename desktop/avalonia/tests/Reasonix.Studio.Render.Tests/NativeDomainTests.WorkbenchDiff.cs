using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>A two-hunk patch shared with the screenshot scenario.</summary>
    private const string WorkbenchDiffFixture = """
        --- a/README.md
        +++ b/README.md
        @@ -7,5 +7,6 @@ 工作区
         # Workspace
        -old description
        +new description
        +more details
         kept context

         end of section
        @@ -81,2 +82,2 @@ 说明
        --- a literal prefix
        +++ a literal prefix
         last line
        \ No newline at end of file
        """;

    /// <summary>Checks numbered hunks, the empty state, line navigation and the shared truncated changes view.</summary>
    /// <param name="width">Window width.</param><param name="dark">Whether the dark theme is active.</param>
    /// <returns>The workbench diff interactions.</returns>
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(760, false)]
    [InlineData(760, true)]
    public Task WorkbenchDiffFollowsTheReferenceHunksAndLineActions(int width, bool dark) => WithSettings(
        "workbench",
        (w, reads) =>
        {
            var height = width == 1440 ? 900 : 480;
            w.Width = width;
            w.Height = height;
            w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            w.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var patch = string.Empty;
            var truncated = false;
            reads.Exchange = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri?.AbsolutePath == "/changes/diff"
                ? JsonReply(new JsonObject { ["diff"] = patch, ["truncated"] = truncated })
                : null);

            var editor = w.FindControl<TextBox>("FileEditor")!;
            var diff = w.FindControl<Button>("DiffFile")!;
            var surface = w.FindControl<ScrollViewer>("RenderedFile")!;
            OpenDiff();
            var empty = Assert.Single(surface.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == "没有改动");
            Assert.InRange(empty.TranslatePoint(default, surface)!.Value.X, 19, 21);
            Assert.InRange(empty.TranslatePoint(default, surface)!.Value.Y, 15, 17);
            Capture("empty");

            patch = WorkbenchDiffFixture + "\n";
            OpenDiff();
            var lines = surface.GetVisualDescendants().OfType<Button>().Where(IsLine).ToArray();
            Assert.Equal(24, lines.Length);
            var blank = string.Empty;
            Assert.Equal(new[] { blank, blank, "7", "7", "8", blank, blank, "8" }, lines.Take(8).Select(button => button.Content as string));
            Assert.Equal(new[] { blank, blank, "81", blank, blank, "82", "82", "83" }, lines.Skip(16).Select(button => button.Content as string));
            Assert.InRange(lines[2].Bounds.Width, 29.9, 30.1);
            Assert.InRange(lines[2].Bounds.Height, 19.1, 19.3);
            var number = Assert.Single(lines[2].GetVisualDescendants().OfType<TextBlock>());
            Assert.Equal(dark ? Color.Parse("#8e9289") : Color.Parse("#666a63"), Assert.IsAssignableFrom<ISolidColorBrush>(number.Foreground).Color);
            Assert.Equal(default, surface.Padding);
            var textBody = Assert.Single(surface.GetVisualDescendants().OfType<SelectableTextBlock>(), text => text.IsEffectivelyVisible);
            var code = string.Concat(textBody.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(run => run.Text));
            Assert.Contains("-- a literal prefix", code);
            Assert.Contains("++ a literal prefix", code);
            Assert.DoesNotContain("No newline", code);
            Capture("numbered");

            lines[4].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(editor.IsEffectivelyVisible);
            Assert.Contains("active", w.FindControl<Button>("EditFile")!.Classes);
            Capture("line-open");

            truncated = true;
            ClickAt(w, "action:workbench.changes-page");
            PumpNavigation(() => surface.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible && text.Name == "Truncated"));
            Assert.Contains(surface.GetVisualDescendants().OfType<Button>(), button => IsLine(button) && Equals(button.Content, "81"));
            Capture("truncated");

            static bool IsLine(Button button) => AutomationProperties.GetAutomationId(button) == "action:workbench.change-open";

            void OpenDiff()
            {
                diff.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpNavigation(() => !editor.IsEffectivelyVisible && surface.IsEffectivelyVisible);
                w.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }

            void Capture(string state) => CaptureFlow(w, $"native-diff-{state}-{width}x{height}");
        });
}
