using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Compares the visible toolbar, selected file and new-tab surface with the measured Electron header.</summary>
    /// <param name="width">The window width.</param><param name="dark">Whether to use the dark theme.</param><returns>The workbench interactions.</returns>
    [Theory]
    [InlineData(1440, false)]
    [InlineData(760, true)]
    public Task WorkbenchHeaderKeepsElectronTabAndButtonGeometry(int width, bool dark) => WithSettings(
        "workbench",
        (window, _) =>
        {
            var height = width == 1440 ? 900 : 480;
            window.Width = width;
            window.Height = height;
            window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            Measure();

            var header = window.GetVisualDescendants().OfType<Grid>().Single(grid => AutomationProperties.GetAutomationId(grid) == "class:workbench-tabs");
            var files = ControlAt<Button>(window, "action:workbench.files");
            var expand = ControlAt<Button>(window, "action:browser.full");
            var add = ControlAt<Button>(window, "action:workbench.new");
            var dismiss = ControlAt<Button>(window, "action:workbench.dismiss");
            var tab = FileTab();
            Assert.Equal(42, header.Bounds.Height);
            Assert.Equal(53, files.Bounds.Width);
            Assert.Contains("active", files.Classes);
            Assert.Equal(new Point(8, 7), files.TranslatePoint(default, header));
            Assert.Equal(new Point(63, 7), tab.TranslatePoint(default, header));
            Assert.InRange(tab.Bounds.Width, 126, 141);
            Assert.Equal(35, tab.Bounds.Height);
            Assert.Contains(tab.GetVisualDescendants().OfType<Viewbox>(), view => view.Name == "TabIcon" && view.Bounds.Width == 14);
            Assert.All(new[] { add, expand, dismiss }, button =>
            {
                Assert.Equal(new Size(28, 28), button.Bounds.Size);
                Assert.Equal(6.5, button.TranslatePoint(default, header)!.Value.Y);
            });
            Assert.Equal(header.Bounds.Width - 34, dismiss.TranslatePoint(default, header)!.Value.X);
            Capture("file");

            ClickAt(window, "action:workbench.files");
            Assert.DoesNotContain("active", files.Classes);
            Capture("files-hidden");
            ClickAt(window, "action:workbench.files");
            ClickAt(window, "action:browser.full");
            Assert.Contains("active", expand.Classes);
            Measure();
            Assert.Equal(width == 1440 ? 1164 : 748, header.Bounds.Width);
            Capture("expanded");

            ClickAt(window, "action:workbench.new");
            Measure();
            Assert.DoesNotContain("active", files.Classes);
            Assert.DoesNotContain("active", FileTab().Classes);
            var launcher = window.FindControl<StackPanel>("FileTabs")!.Children.Single(tabItem => tabItem.Classes.Contains("launcher"));
            Assert.InRange(launcher.Bounds.Width, 104, 108);
            Assert.Contains(launcher.GetVisualDescendants().OfType<TextBlock>(), label => label.Text == "新标签" && label.IsEffectivelyVisible);
            Capture("with-launcher");

            ClickAt(window, "action:workbench.tab");
            Assert.Contains("active", FileTab().Classes);
            window.FindControl<Button>("EditFile")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            window.FindControl<TextBox>("FileEditor")!.Text = "Unsaved tab appearance fixture";
            Measure();
            Assert.Contains(FileTab().GetVisualDescendants().OfType<Border>(), dot => dot.Name == "DirtyMark" && dot.IsEffectivelyVisible && dot.Bounds.Width == 7);
            Capture("dirty");

            Control FileTab() => window.FindControl<StackPanel>("FileTabs")!.Children.Single(tabItem => Equals(tabItem.Tag, "README.md"));
            void Capture(string state)
            {
                CaptureFlow(window, $"native-{state}-{width}x{height}");
                CaptureFlow(window, $"native-header-{state}-{width}x{height}", region: header);
            }

            void Measure()
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
            }
        });
}
