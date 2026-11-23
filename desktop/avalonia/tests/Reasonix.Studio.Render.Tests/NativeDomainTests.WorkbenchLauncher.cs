using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Controls;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Keeps the selected surface while toggling the explicit new-tab launcher.</summary>
    /// <param name="width">Window width.</param><param name="dark">Whether to use the dark theme.</param>
    /// <returns>The launcher and explorer interactions.</returns>
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(760, false)]
    [InlineData(760, true)]
    public Task WorkbenchLauncherRestoresTheSamePreviewAndExplorer(int width, bool dark) => WithSettings(
        "workbench",
        (w, _) =>
        {
            var height = width == 1440 ? 900 : 480;
            w.Width = width;
            w.Height = height;
            w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            w.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var explorer = w.FindControl<Control>("ExplorerPane")!;
            var preview = w.FindControl<ScrollViewer>("RenderedFile")!;
            var launcher = w.FindControl<Control>("WorkbenchStart")!;
            var add = ControlAt<Button>(w, "action:workbench.new");
            var document = preview.Content;
            Assert.True(preview.IsEffectivelyVisible);
            Assert.True(explorer.IsEffectivelyVisible);
            Capture("file");

            ClickAt(w, "action:workbench.new");
            Assert.True(launcher.IsEffectivelyVisible);
            Assert.False(preview.IsEffectivelyVisible);
            Assert.False(explorer.IsEffectivelyVisible);
            Assert.Same(document, preview.Content);
            var files = ControlAt<Button>(w, "action:workbench.launch-files");
            Assert.Same(files, w.FocusManager!.GetFocusedElement());
            var first = files.TranslatePoint(default, launcher)!.Value;
            Assert.InRange(first.X, 19, 21);
            Assert.InRange(first.Y, 56, 59);
            Assert.InRange(files.Bounds.Width, 379, 381);
            Assert.Equal(56, files.Bounds.Height);
            Capture("launcher");

            ClickAt(w, "action:workbench.new");
            Assert.False(launcher.IsEffectivelyVisible);
            Assert.True(preview.IsEffectivelyVisible);
            Assert.True(explorer.IsEffectivelyVisible);
            Assert.Same(document, preview.Content);
            Capture("restored");

            ClickAt(w, "action:workbench.files");
            Assert.False(explorer.IsEffectivelyVisible);
            ClickAt(w, "action:workbench.new");
            w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
            Dispatcher.UIThread.RunJobs();
            Assert.False(launcher.IsEffectivelyVisible);
            Assert.False(explorer.IsEffectivelyVisible);
            Assert.Same(add, w.FocusManager!.GetFocusedElement());
            Assert.Same(document, preview.Content);
            Capture("escape");

            ClickAt(w, "action:workbench.new");
            ClickAt(w, "action:workbench.launch-files");
            Assert.False(launcher.IsEffectivelyVisible);
            Assert.True(explorer.IsEffectivelyVisible);
            Assert.True(preview.IsEffectivelyVisible);
            Assert.Same(document, preview.Content);
            Assert.Same(w.FindControl<TextBox>("WorkbenchSearch"), w.FocusManager!.GetFocusedElement());
            Capture("explorer");

            w.FindControl<Button>("EditFile")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var editor = w.FindControl<TextBox>("FileEditor")!;
            editor.Text = "Launcher draft fixture";
            editor.CaretIndex = 8;
            ClickAt(w, "action:workbench.new");
            ClickAt(w, "action:workbench.new");
            Assert.True(editor.IsEffectivelyVisible);
            Assert.Equal("Launcher draft fixture", editor.Text);
            Assert.Equal(8, editor.CaretIndex);
            Assert.True(ControlAt<Button>(w, "action:workbench.save").IsEnabled);
            Capture("editor-restored");

            void Capture(string state) => CaptureFlow(w, $"native-launcher-{state}-{width}x{height}");
        });

    /// <summary>Temporarily covers a browser without releasing its guest.</summary>
    /// <returns>The browser and launcher interactions.</returns>
    [Fact]
    public Task WorkbenchLauncherKeepsTheSelectedBrowserAttached() => WithSettings("workbench", (w, _) =>
    {
        ClickAt(w, "action:workbench.new");
        ClickAt(w, "action:workbench.launch-browser");
        var host = w.FindControl<StudioSurfaceHost>("ManualBrowserHost")!;
        var explorer = w.FindControl<Control>("ExplorerPane")!;
        var browser = Assert.IsAssignableFrom<Control>(host.Content);
        Assert.True(host.IsEffectivelyVisible);
        Assert.False(explorer.IsEffectivelyVisible);
        ClickAt(w, "action:workbench.new");
        Assert.False(host.IsEffectivelyVisible);
        Assert.True(browser.IsAttachedToVisualTree());
        Assert.Same(browser, host.Content);
        ClickAt(w, "action:workbench.files");
        Assert.True(host.IsEffectivelyVisible);
        Assert.True(explorer.IsEffectivelyVisible);
        Assert.Same(browser, host.Content);
        Assert.Single(host.Children);

        ClickAt(w, "action:workbench.new");
        var tab = w.FindControl<StackPanel>("FileTabs")!.GetVisualDescendants().OfType<Button>()
            .Single(button => AutomationProperties.GetAutomationId(button) == "action:workbench.browser-tab");
        var menu = Assert.IsType<MenuFlyout>(tab.ContextFlyout);
        menu.Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "action:workbench.close-all-browsers")
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.FindControl<Control>("WorkbenchStart")!.IsEffectivelyVisible);
        Assert.Empty(host.Children);
        ClickAt(w, "action:workbench.new");
        Assert.True(w.FindControl<ScrollViewer>("RenderedFile")!.IsEffectivelyVisible);
    });

    /// <summary>Distinguishes the empty workbench from an explicitly selected new tab.</summary>
    /// <returns>The initial launcher and empty explorer interactions.</returns>
    [Fact]
    public Task WorkbenchLauncherOffersAChoiceWithoutOpeningANewTab() => WithSettings("conversation", (w, _) =>
    {
        w.FindControl<MenuItem>("WorkbenchView")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var tabs = w.FindControl<StackPanel>("FileTabs")!;
        var launcher = w.FindControl<Control>("WorkbenchStart")!;
        Assert.True(launcher.IsEffectivelyVisible);
        Assert.DoesNotContain(tabs.Children, tab => tab.Classes.Contains("launcher"));
        ClickAt(w, "action:workbench.launch-files");
        Assert.False(launcher.IsEffectivelyVisible);
        Assert.True(w.FindControl<Control>("WorkbenchEmpty")!.IsEffectivelyVisible);
        ClickAt(w, "action:workbench.files");
        Assert.True(launcher.IsEffectivelyVisible);
        ClickAt(w, "action:workbench.new");
        Assert.Contains(tabs.Children, tab => tab.Classes.Contains("launcher"));
        ClickAt(w, "action:workbench.new");
        Assert.True(launcher.IsEffectivelyVisible);
        Assert.DoesNotContain(tabs.Children, tab => tab.Classes.Contains("launcher"));
    });
}
