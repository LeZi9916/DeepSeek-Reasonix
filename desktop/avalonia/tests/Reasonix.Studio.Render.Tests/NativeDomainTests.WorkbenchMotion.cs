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
                var rotate = Assert.IsType<RotateTransform>(icon.RenderTransform);
                if (!changes.IsVisible)
                {
                    fold.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await WaitForSettingsFrame(() => rotate.Angle >= -0.1 && !rotate.IsAnimating(RotateTransform.AngleProperty)).ConfigureAwait(true);
                }

                var passedThroughIntermediateAngle = false;
                rotate.PropertyChanged += Observe;
                try
                {
                    fold.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.False(changes.IsVisible);
                    await WaitForSettingsFrame(() => rotate.Angle <= -89.9 && !rotate.IsAnimating(RotateTransform.AngleProperty)).ConfigureAwait(true);
                    Assert.True(passedThroughIntermediateAngle);
                }
                finally
                {
                    rotate.PropertyChanged -= Observe;
                }

                void Observe(object? sender, AvaloniaPropertyChangedEventArgs e)
                {
                    if (e.Property == RotateTransform.AngleProperty && rotate.Angle is < -1 and > -89)
                    {
                        passedThroughIntermediateAngle = true;
                    }
                }
            },
            allowMotion: true);
}
