using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
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
    /// <summary>Compares the file toolbar, the editor gutter, and save state with the Electron captures.</summary>
    /// <param name="width">Window width.</param><param name="dark">Whether to use the dark appearance.</param>
    /// <returns>The file toolbar and editor interactions.</returns>
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(760, false)]
    [InlineData(760, true)]
    public Task WorkbenchEditorFollowsTheReferenceToolbarAndLineGutter(int width, bool dark) => WithSettings(
        "workbench",
        (w, reads) =>
        {
            var height = width == 1440 ? 900 : 480;
            w.Width = width;
            w.Height = height;
            w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            w.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var bar = w.GetVisualDescendants().OfType<Grid>().Single(grid => AutomationProperties.GetAutomationId(grid) == "class:workbench-filebar");
            var modes = w.FindControl<Border>("FileModeSelector")!;
            var status = w.FindControl<Control>("FileStatus")!;
            var save = ControlAt<Button>(w, "action:workbench.save");
            var editor = w.FindControl<WorkbenchFileEditor>("FileEditor")!;
            var preview = w.FindControl<ScrollViewer>("RenderedFile")!;
            Assert.Equal(40, bar.Bounds.Height);
            Assert.InRange(modes.Bounds.Height, 28, 30);
            Assert.Equal(new Size(75, 32), save.Bounds.Size);
            Assert.False(save.IsEnabled);
            Assert.False(status.IsEffectivelyVisible);
            Assert.Equal(new Thickness(32, 24, 32, 48), preview.Padding);
            Capture("reading");

            w.FindControl<Button>("EditFile")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            w.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(status.IsEffectivelyVisible);
            Assert.Equal(28, status.Bounds.Height);
            Assert.Equal(12, editor.FontSize);
            var presenter = editor.TextArea.TextView;
            Assert.Equal(default, editor.BorderThickness);
            Assert.InRange(presenter.TranslatePoint(default, editor)!.Value.X, 20, 42);
            Assert.InRange(presenter.DefaultLineHeight, 17, 22);
            Capture("editing");

            var draft = string.Join('\n', Enumerable.Range(1, 35).Select(i => $"工作台对照 {i:D2}"));
            editor.Text = draft;
            editor.CaretOffset = draft.Length;
            var scroller = editor.GetVisualDescendants().OfType<ScrollViewer>().First();
            Dispatcher.UIThread.RunJobs();
            scroller.Offset = default;
            Dispatcher.UIThread.RunJobs();
            Assert.True(save.IsEnabled);
            Assert.NotEqual(Colors.Transparent, (save.Background as ISolidColorBrush)?.Color);
            Assert.InRange(presenter.TranslatePoint(default, editor)!.Value.X, 20, 49);
            Capture("dirty");

            scroller.Offset = new Vector(0, 198);
            Dispatcher.UIThread.RunJobs();
            Assert.True(scroller.Offset.Y > 0);
            Assert.True(presenter.VerticalOffset > 0);
            Capture("scrolled");

            reads.Exchange = async request =>
            {
                if (request.RequestUri?.AbsolutePath == "/changes/diff")
                {
                    return JsonReply(new JsonObject { ["diff"] = string.Empty });
                }

                if (request.Method != HttpMethod.Put || request.RequestUri?.AbsolutePath != "/workspace/file")
                {
                    return null;
                }

                var contents = JsonNode.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false))!;
                Assert.Equal(draft, (string?)contents["content"]);
                var file = reads.Read<JsonObject>("/workspace/file");
                file["content"] = draft;
                file["revision"] = "saved-editor-fixture";
                return JsonReply(file);
            };
            if (width == 760)
            {
                editor.TextArea.Focus();
                var primary = System.OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
                w.KeyPress(Key.S, primary, PhysicalKey.S, string.Empty);
            }
            else
            {
                ClickAt(w, "action:workbench.save");
            }

            PumpNavigation(() => !w.FindControl<TextBlock>("FileDirty")!.IsVisible);
            Assert.False(save.IsEnabled);
            Capture("saved");

            w.FindControl<Button>("DiffFile")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpNavigation(() => !editor.IsEffectivelyVisible);
            Assert.False(status.IsEffectivelyVisible);
            Capture("diff");

            void Capture(string state) => CaptureFlow(w, $"native-editor-{state}-{width}x{height}");
        });

    /// <summary>Granted files keep the root and read-only actions when changing modes.</summary>
    /// <returns>The editor and read-only toolbar interaction.</returns>
    [Fact]
    public Task WorkbenchEditorShowsTheGrantOnReadOnlyFiles() => WithSettings(
        "workbench",
        (w, reads) =>
        {
            reads.Exchange = request =>
            {
                if (request.Method != HttpMethod.Get || request.RequestUri?.AbsolutePath != "/workspace/file")
                {
                    return Task.FromResult<HttpResponseMessage?>(null);
                }

                var file = reads.Read<JsonObject>("/workspace/file");
                file["root"] = "/granted";
                file["readOnly"] = true;
                return Task.FromResult<HttpResponseMessage?>(JsonReply(file));
            };
            ClickAt(w, "action:workbench.tab");
            var breadcrumb = w.FindControl<TextBlock>("Breadcrumb")!;
            var edit = w.FindControl<Button>("EditFile")!;
            var editor = w.FindControl<WorkbenchFileEditor>("FileEditor")!;
            var save = w.FindControl<Button>("SaveFile")!;
            Assert.Equal("/granted/README.md", ToolTip.GetTip(breadcrumb));
            Assert.True(w.FindControl<Control>("ReadOnlyBadge")!.IsEffectivelyVisible);
            Assert.False(save.IsEffectivelyVisible);
            Assert.False(w.FindControl<Button>("DiffFile")!.IsEffectivelyVisible);
            Assert.False(w.FindControl<Button>("FileMore")!.IsEffectivelyVisible);
            Assert.Equal("文件", edit.Content);
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(editor.IsEffectivelyVisible);
            Assert.True(editor.IsReadOnly);
            Assert.False(save.IsEffectivelyVisible);
            editor.Text = "first\r\nsecond\r\n";
            editor.CaretOffset = editor.Text.Length;
            Assert.Contains("3", w.FindControl<TextBlock>("FileLine")!.Text);
        });
}
