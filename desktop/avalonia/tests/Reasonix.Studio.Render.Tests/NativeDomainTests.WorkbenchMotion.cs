using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks the workbench section-fold chevron's reference rotation transition.</summary>
    /// <returns>The fold motion check.</returns>
    [Fact]
    public Task WorkbenchSectionFoldRotatesThroughTheReferenceCurve() =>
        WithSettingsAsync(
            "workbench",
            async (window, _) =>
            {
                await WaitForMotionFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
                var icon = window.GetVisualDescendants().OfType<Viewbox>().Single(view => view.Name == "ChangesFoldIcon");
                var fold = window.GetVisualDescendants().OfType<Button>()
                    .Single(button => AutomationProperties.GetAutomationId(button) == "action:workbench.section-fold");
                var changes = window.GetVisualDescendants().OfType<StackPanel>().Single(panel => panel.Name == "Changes");
                fold.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (changes.IsVisible)
                {
                    fold.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }

                await Task.Delay(20).ConfigureAwait(true);
                await WaitForSettingsFrame(() => Angle(icon) is < -1 and > -89).ConfigureAwait(true);
                await WaitForSettingsFrame(() => Angle(icon) <= -89.9).ConfigureAwait(true);

                static double Angle(Control control) => (control.RenderTransform as RotateTransform)?.Angle ?? 0;
            },
            allowMotion: true);
}
