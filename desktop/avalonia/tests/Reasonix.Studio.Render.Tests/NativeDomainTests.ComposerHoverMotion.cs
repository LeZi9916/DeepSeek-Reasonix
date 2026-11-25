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
    /// <summary>Checks that the composer pills change ink and fill without lifting.</summary>
    /// <returns>The Electron composer button hover check.</returns>
    [Fact]
    public Task ComposerPillsChangeInkAndFillWithoutLifting() =>
        WithSettingsAsync(
            "conversation",
            async (window, _) =>
            {
                await WaitForSettingsFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
                var plan = window.FindControl<Button>("PlanMode")!;
                var icon = plan.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().First();
                var resting = Assert.IsAssignableFrom<ISolidColorBrush>(icon.Stroke).Color;
                Assert.True(window.TryFindResource("TextBrush", window.ActualThemeVariant, out var text));
                var hover = Assert.IsAssignableFrom<ISolidColorBrush>(text).Color;
                var brushes = plan.Transitions!.OfType<BrushTransition>().ToArray();
                Assert.Equal(2, brushes.Length);
                Assert.All(brushes, transition => Assert.Equal(TimeSpan.FromMilliseconds(120), transition.Duration));
                Assert.Empty(plan.Transitions!.OfType<TransformOperationsTransition>());

                window.MouseMove(plan.TranslatePoint(new Point(12, 12), window)!.Value);
                await WaitForSettingsFrame(() => (icon.Stroke as ISolidColorBrush)?.Color == hover).ConfigureAwait(true);
                Assert.Equal(0, plan.RenderTransform?.Value.M32 ?? 0);
                window.MouseMove(new Point(620, 160));
                await WaitForSettingsFrame(() => (icon.Stroke as ISolidColorBrush)?.Color == resting).ConfigureAwait(true);
            },
            workspacePreferences: new()
            {
                ShowModePicker = true,
            },
            allowMotion: true);

    /// <summary>Uses the Electron chrome foreground at the actual workbench icon.</summary>
    /// <returns>The workbench icon's hover and rest colours.</returns>
    [Fact]
    public Task ChromeHoverBrightensAndRestoresTheVisibleIcon() => WithSettingsAsync(
        "conversation",
        async (window, _) =>
        {
            await WaitForSettingsFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
            var button = window.FindControl<Button>("ViewSwitch")!;
            var path = button.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single();
            var resting = Assert.IsAssignableFrom<ISolidColorBrush>(path.Stroke).Color;
            Assert.True(window.TryFindResource("TextBrush", window.ActualThemeVariant, out var text));
            var hovered = Assert.IsAssignableFrom<ISolidColorBrush>(text).Color;
            Assert.NotEqual(hovered, resting);
            window.MouseMove(button.TranslatePoint(new Point(12, 12), window)!.Value);
            await WaitForSettingsFrame(() => (path.Stroke as ISolidColorBrush)?.Color == hovered).ConfigureAwait(true);
            window.MouseMove(new Point(620, 160));
            await WaitForSettingsFrame(() => (path.Stroke as ISolidColorBrush)?.Color == resting).ConfigureAwait(true);
        },
        allowMotion: true);

    /// <summary>Checks the translucent Electron meter tint without a button fade.</summary>
    /// <param name="name">The metric button.</param>
    /// <returns>The actual hover and exit samples.</returns>
    [Theory]
    [InlineData("SpeedToggle")]
    [InlineData("CacheToggle")]
    [InlineData("ContextToggle")]
    public Task MeterHighlightsMatchTheReferenceWithoutAnimating(string name) => WithSettingsAsync(
        "conversation",
        async (window, _) =>
        {
            await WaitForSettingsFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
            var button = window.FindControl<Button>(name)!;
            Assert.True(window.TryFindResource(name == "ContextToggle" ? "OverlayBrush" : "TextBrush", window.ActualThemeVariant, out var target));
            var expected = Assert.IsAssignableFrom<ISolidColorBrush>(target).Color;
            window.MouseMove(button.TranslatePoint(new Point(8, 8), window)!.Value);
            var brush = Assert.IsAssignableFrom<ISolidColorBrush>(button.Background);
            Assert.Equal(expected, brush.Color);
            Assert.Equal(name == "ContextToggle" ? 1 : 0.07, brush.Opacity, 4);
            Assert.False(button.IsAnimating(Button.BackgroundProperty));
            window.MouseMove(new Point(620, 160));
            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color);
        },
        allowMotion: true);

    /// <summary>Checks the workbench frame that paints the file tab’s hover background.</summary>
    /// <returns>The mounted tab and its brush transition.</returns>
    [Fact]
    public Task WorkbenchTabUsesTheElectronBrushCurve() => WithSettingsAsync(
        "workbench",
        async (window, _) =>
        {
            await WaitForSettingsFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
            var tab = window.FindControl<StackPanel>("FileTabs")!.Children
                .OfType<Reasonix.Studio.Shell.Workbench.WorkbenchHeaderTab>().Single(control => Equals(control.Tag, "README.md"));
            var frame = tab.FindControl<Border>("Frame")!;
            var brushes = frame.Transitions?.OfType<BrushTransition>().ToArray() ?? Array.Empty<BrushTransition>();
            Assert.Contains(brushes, transition => transition.Property == Border.BackgroundProperty && transition.Duration == TimeSpan.FromMilliseconds(120));
            Assert.Contains(brushes, transition => transition.Property == Border.BorderBrushProperty && transition.Duration == TimeSpan.FromMilliseconds(120));
        },
        allowMotion: true);

    /// <summary>Matches the Electron dismiss button’s immediate hover colours.</summary>
    /// <returns>The workbench header button interaction.</returns>
    [Fact]
    public Task WorkbenchDismissHighlightSwitchesImmediately() => WithSettingsAsync(
        "workbench",
        async (window, _) =>
        {
            await WaitForSettingsFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
            var close = window.FindControl<Button>("CloseWorkbench")!;
            window.UpdateLayout();
            var rest = Assert.IsAssignableFrom<ISolidColorBrush>(close.Background).Color;
            Assert.True(window.TryFindResource("OverlayBrush", window.ActualThemeVariant, out var overlay));
            var hover = Assert.IsAssignableFrom<ISolidColorBrush>(overlay).Color;

            var size = close.Bounds.Size;
            window.MouseMove(close.TranslatePoint(new Point(size.Width / 2, size.Height / 2), window)!.Value);
            Assert.Equal(hover, Assert.IsAssignableFrom<ISolidColorBrush>(close.Background).Color);
            Assert.False(close.IsAnimating(Button.BackgroundProperty));
            window.MouseMove(new Point(620, 160));
            Assert.Equal(rest, Assert.IsAssignableFrom<ISolidColorBrush>(close.Background).Color);
        },
        allowMotion: true);

    /// <summary>Observes the hover background painted by buttons with different templates.</summary>
    /// <param name="name">The mounted button.</param><param name="standardTemplate">Whether Fluent owns the background.</param>
    /// <returns>The pointer entry and exit regression.</returns>
    [Theory]
    [InlineData("ViewSwitch", true)]
    [InlineData("Attach", false)]
    public Task ShellButtonHighlightsEaseInAndOut(string name, bool standardTemplate) =>
        WithSettingsAsync(
            "conversation",
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
                var resting = Paint();
                var samples = new List<(Color Color, double Opacity)>();
                surface.PropertyChanged += (_, change) =>
                {
                    if (change.Property == property)
                    {
                        samples.Add(Paint());
                    }
                };

                window.MouseMove(button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value);
                await WaitForMotionFrame(() => Paint() != resting && !surface.IsAnimating(property)).ConfigureAwait(true);
                var hovered = Paint();
                Assert.Contains(samples, value => value != resting && value != hovered);

                Assert.All(samples, paint => Assert.Equal(hovered.Color, paint.Color));

                samples.Clear();
                window.MouseMove(new Point(600, 140));
                await WaitForMotionFrame(() => Paint() == resting && !surface.IsAnimating(property)).ConfigureAwait(true);
                Assert.Contains(samples, value => value != resting && value != hovered);

                Assert.All(samples, paint => Assert.Equal(hovered.Color, paint.Color));

                (Color Color, double Opacity) Paint() =>
                    surface.GetValue(property) is ISolidColorBrush brush ? (brush.Color, brush.Opacity) : (Colors.Transparent, 0);
            },
            allowMotion: true);
}
