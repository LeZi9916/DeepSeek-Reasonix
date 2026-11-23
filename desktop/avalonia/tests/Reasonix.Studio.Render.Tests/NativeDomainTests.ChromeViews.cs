using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Reasonix.Studio.State;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Opens and closes each pane directly from its top-bar button.</summary>
    /// <param name="width">The viewport width.</param><param name="dark">Whether the theme is dark.</param>
    /// <returns>The real top-bar interaction.</returns>
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(760, false)]
    [InlineData(760, true)]
    public Task WindowChromeShowsSeparateAnalysisAndWorkbenchButtons(int width, bool dark) => WithSettings(
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
            var analysis = w.FindControl<Button>("ChromeAnalysis");
            Assert.NotNull(analysis);
            Assert.False(analysis.IsVisible);
            available = true;
            w.ShowState(new SessionState { Running = true });
            w.ShowState(Reasonix.Studio.Testing.ReferenceConversation.Create());
            Dispatcher.UIThread.RunJobs();
            Assert.True(analysis.IsEffectivelyVisible);
            var workbench = w.FindControl<Button>("ViewSwitch")!;
            Assert.True(workbench.IsEffectivelyVisible);
            Capture("conversation");

            analysis.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(w.FindControl<ScrollViewer>("AnalysisPanel")!.IsVisible);
            Assert.Contains("active-view", analysis.Classes);
            Capture("analysis");
            analysis.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(w.FindControl<ScrollViewer>("AnalysisPanel")!.IsVisible);
            Assert.DoesNotContain("active-view", analysis.Classes);

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
