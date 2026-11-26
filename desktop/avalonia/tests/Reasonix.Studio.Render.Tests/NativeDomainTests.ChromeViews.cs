using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.State;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Keeps the top bar fixed while analysis becomes available in the view menu.</summary>
    /// <param name="width">The viewport width.</param><param name="dark">Whether the theme is dark.</param>
    /// <returns>The real top-bar interaction.</returns>
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(760, false)]
    [InlineData(760, true)]
    public Task WindowChromeKeepsAnalysisInTheWorkbenchMenu(int width, bool dark) => WithSettings(
        "conversation",
        (w, reads) =>
        {
            var height = width == 1440 ? 900 : 480;
            w.Width = width;
            w.Height = height;
            w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var available = false;
            reads.Exchange = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath != "/trajectory"
                ? null
                : JsonReply(available ? reads.Read<JsonObject>("/trajectory") : new JsonObject { ["events"] = new JsonArray() }));
            w.ShowState(new SessionState { Running = true });
            w.ShowState(new SessionState());
            Dispatcher.UIThread.RunJobs();
            var workbench = w.FindControl<Button>("ViewSwitch")!;
            var menu = Assert.IsType<MenuFlyout>(workbench.ContextFlyout);
            var analysis = Assert.IsType<MenuItem>(menu.Items[1]);
            Assert.False(analysis.IsVisible);
            w.UpdateLayout();
            var find = w.FindControl<Button>("FindButton")!;
            var initialX = find.TranslatePoint(default, w)!.Value.X;
            Assert.Equal(1, w.FindControl<TextBlock>("CrumbTitle")!.Opacity);
            var project = w.FindControl<TextBlock>("ProjectName")!;
            Assert.Equal(FontWeight.Normal, project.FontWeight);
            Assert.Equal(520, project.MaxWidth);
            Assert.Equal(TextTrimming.CharacterEllipsis, project.TextTrimming);
            available = true;
            w.ShowState(new SessionState { Running = true });
            w.ShowState(Reasonix.Studio.Testing.ReferenceConversation.Create());
            Dispatcher.UIThread.RunJobs();
            w.UpdateLayout();
            Assert.True(analysis.IsVisible);
            Assert.Equal(initialX, find.TranslatePoint(default, w)!.Value.X);
            Assert.Null(w.FindControl<Button>("ChromeAnalysis"));
            Assert.True(workbench.IsEffectivelyVisible);
            Assert.Contains(workbench.GetVisualDescendants().OfType<Viewbox>(), view => view.Bounds.Width == 16);
            Capture("conversation");

            analysis.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(w.FindControl<ScrollViewer>("AnalysisPanel")!.IsVisible);
            Capture("analysis");
            w.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.D2,
                KeyModifiers = System.OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control,
            });
            Dispatcher.UIThread.RunJobs();
            Assert.False(w.FindControl<ScrollViewer>("AnalysisPanel")!.IsVisible);

            workbench.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(w.FindControl<Control>("WorkbenchPanel")!.IsVisible);
            Assert.Contains("active-view", workbench.Classes);
            Capture("workbench");
            workbench.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(w.FindControl<Control>("WorkbenchPanel")!.IsVisible);
            Assert.DoesNotContain("active-view", workbench.Classes);

            void Capture(string view) => CaptureFlow(w, $"global-chrome-{view}-{width}x{height}");
        });
}
