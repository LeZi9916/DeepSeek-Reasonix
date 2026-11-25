using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks the composer mode pill's reference 120ms hover transitions.</summary>
    /// <returns>The composer pill hover check.</returns>
    [Fact]
    public Task ComposerPillsCarryTheReferenceHoverTransitions() =>
        WithSettingsAsync(
            "conversation",
            async (window, _) =>
            {
                await WaitForSettingsFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
                var plan = window.FindControl<Button>("PlanMode")!;
                Assert.Contains("compose-pill", plan.Classes);
                var brushes = plan.Transitions!.OfType<BrushTransition>().ToArray();
                var transform = Assert.Single(plan.Transitions!.OfType<TransformOperationsTransition>());
                Assert.Equal(2, brushes.Length);
                Assert.All(brushes, transition => Assert.Equal(TimeSpan.FromMilliseconds(120), transition.Duration));
                Assert.Equal(TimeSpan.FromMilliseconds(120), transform.Duration);
            },
            allowMotion: true);

    /// <summary>Observes the hover background painted by buttons with different templates.</summary>
    /// <param name="name">The mounted button.</param><param name="standardTemplate">Whether Fluent owns the background.</param>
    /// <returns>The pointer entry and exit regression.</returns>
    [Theory]
    [InlineData("ViewSwitch", true)]
    [InlineData("SpeedToggle", false)]
    [InlineData("CloseWorkbench", false)]
    public Task ShellButtonHighlightsEaseInAndOut(string name, bool standardTemplate) =>
        WithSettingsAsync(
            name == "CloseWorkbench" ? "workbench" : "conversation",
            async (window, _) =>
            {
                await WaitForMotionFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
                var button = window.FindControl<Button>(name)!;
                window.UpdateLayout();
                var presenter = standardTemplate
                    ? button.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().Single(control => control.Name == "PART_ContentPresenter")
                    : null;
                var surface = (Control?)presenter ?? button;
                var property = standardTemplate ? Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty : Button.BackgroundProperty;
                var resting = ColorOfSurface();
                var samples = new List<Color>();
                surface.PropertyChanged += (_, change) =>
                {
                    if (change.Property == property)
                    {
                        samples.Add(ColorOfSurface());
                    }
                };

                window.MouseMove(button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value);
                await WaitForMotionFrame(() => ColorOfSurface() != resting && !surface.IsAnimating(property)).ConfigureAwait(true);
                var hovered = ColorOfSurface();
                Assert.Contains(samples, value => value != resting && value != hovered);

                samples.Clear();
                window.MouseMove(new Point(600, 140));
                await WaitForMotionFrame(() => ColorOfSurface() == resting && !surface.IsAnimating(property)).ConfigureAwait(true);
                Assert.Contains(samples, value => value != resting && value != hovered);

                Color ColorOfSurface() => (surface.GetValue(property) as ISolidColorBrush)?.Color ?? Colors.Transparent;
            },
            allowMotion: true);
}
