using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Settings;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Replays the reference section entrance through the real navigation control.</summary>
    /// <param name="dark">Whether to use the dark palette.</param>
    /// <returns>The section entrance check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SettingsSectionEntersWithTheReferenceColumnMotion(bool dark) =>
        WithSettingsAsync(
            "settings-common",
            async (w, _) =>
            {
                w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                var scheme = dark ? "dark" : "light";
                await WaitForSettingsFrame(() => w.Classes.Contains("motion")).ConfigureAwait(true);
                var content = w.FindControl<ContentControl>("SettingsContent")!;
                await WaitForSettingsFrame(() => !content.IsAnimating(Visual.OpacityProperty)).ConfigureAwait(true);

                var nav = w.FindControl<ListBox>("SettingsNav")!;
                nav.SelectedItem = nav.Items.Cast<object>().Single(item =>
                    item.ToString() == MessageCatalog.Get("common.models", InterfaceLanguage.Zh));
                Assert.True(content.Content is StackPanel { Children.Count: > 0 });
                Assert.True(content.Opacity >= 0 && content.Opacity < 1);
                Assert.True(content.RenderTransform!.Value.M32 > 0.4);
                CaptureMotionRegion(w, content, -8, 8, 760, 130, $"settings-column-{scheme}-60");
                await WaitForSettingsFrame(() => content.Opacity == 1 &&
                    Math.Abs(content.RenderTransform!.Value.M32) < 0.01).ConfigureAwait(true);
                CaptureMotionRegion(w, content, -8, 8, 760, 130, $"settings-column-{scheme}-settled");
            },
            allowMotion: true);

    /// <summary>Replays the reference page-index scroll and group landing through the settings links.</summary>
    /// <param name="dark">Whether to use the dark palette.</param>
    /// <returns>The landing check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SettingsPageIndexGlidesAndFlashesTheGroup(bool dark) =>
        WithSettingsAsync(
            "settings-model",
            async (w, _) =>
            {
                w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                var scheme = dark ? "dark" : "light";
                await WaitForSettingsFrame(() => w.Classes.Contains("motion")).ConfigureAwait(true);
                var content = w.FindControl<ContentControl>("SettingsContent")!;
                await WaitForSettingsFrame(() => !content.IsAnimating(Visual.OpacityProperty)).ConfigureAwait(true);

                var key = "settings.context_maintenance";
                var link = w.FindControl<WrapPanel>("SettingsLinks")!.Children.OfType<Button>()
                    .First(button => Equals(button.Content, MessageCatalog.Get(key, InterfaceLanguage.Zh)));
                var group = w.FindControl<StackPanel>("SettingsBody")!.GetVisualDescendants()
                    .OfType<SettingsGroup>().First(settings => Equals(settings.Tag, key));
                var ring = group.FindControl<Border>("LandingRing")!;
                var scroller = w.FindControl<ScrollViewer>("SettingsScroller")!;
                var index = w.FindControl<Border>("SettingsIndex")!;
                var before = scroller.Offset.Y;
                var clearance = (index.IsVisible ? index.Bounds.Height : 0) + 16;
                var expected = Math.Clamp(before + group.TranslatePoint(default, scroller)!.Value.Y - clearance, 0, scroller.ScrollBarMaximum.Y);
                link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitForSettingsFrame(() => ring.IsVisible && ring.Opacity is > 0.5 and <= 0.66).ConfigureAwait(true);
                Assert.InRange(scroller.Offset.Y, before, Math.Max(before, expected - 20));
                CaptureMotionRegion(w, group, -10, 10, 900, 100, $"settings-landing-{scheme}-entry");
                await WaitForSettingsFrame(() => scroller.Offset.Y > expected - 1).ConfigureAwait(true);
                CaptureMotionRegion(w, group, -10, 10, 900, 100, $"settings-landing-{scheme}-mid");
                await WaitForSettingsFrame(() => !ring.IsVisible).ConfigureAwait(true);
                Assert.False(ring.IsVisible);
                Assert.Equal(0d, ring.Opacity);
                CaptureMotionRegion(w, group, -10, 10, 900, 100, $"settings-landing-{scheme}-settled");
            },
            allowMotion: true);

    /// <summary>Shows the full landing outline when the destination is already in view.</summary>
    /// <param name="dark">Whether to use the dark palette.</param>
    /// <returns>The visible landing check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SettingsLandingOutlinesAnAlreadyVisibleGroup(bool dark) =>
        WithSettingsAsync(
            "settings-model",
            async (w, _) =>
            {
                w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                var scheme = dark ? "dark" : "light";
                await WaitForSettingsFrame(() => w.Classes.Contains("motion")).ConfigureAwait(true);
                var key = "settings.models_by_use";
                var link = w.FindControl<WrapPanel>("SettingsLinks")!.Children.OfType<Button>()
                    .First(button => Equals(button.Content, MessageCatalog.Get(key, InterfaceLanguage.Zh)));
                var group = w.FindControl<StackPanel>("SettingsBody")!.GetVisualDescendants()
                    .OfType<SettingsGroup>().First(settings => Equals(settings.Tag, key));
                var ring = group.FindControl<Border>("LandingRing")!;
                var scroller = w.FindControl<ScrollViewer>("SettingsScroller")!;
                var index = w.FindControl<Border>("SettingsIndex")!;
                var clearance = (index.IsVisible ? index.Bounds.Height : 0) + 16;
                scroller.Offset = new Vector(0, Math.Clamp(scroller.Offset.Y + group.TranslatePoint(default, scroller)!.Value.Y - clearance, 0, scroller.ScrollBarMaximum.Y));
                Dispatcher.UIThread.RunJobs();
                link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitForSettingsFrame(() => ring.IsVisible && ring.Opacity is > 0.6 and <= 0.66).ConfigureAwait(true);
                CaptureMotionRegion(w, group, -10, 10, 900, 120, $"settings-landing-visible-{scheme}");
                await WaitForSettingsFrame(() => !ring.IsVisible).ConfigureAwait(true);
            },
            allowMotion: true);

    /// <summary>Keeps section changes and page-index jumps still when motion is off.</summary>
    /// <returns>The reduced-motion check.</returns>
    [Fact]
    public Task SettingsMotionHonorsReducedMotion() =>
        WithSettingsAsync(
            "settings-common",
            async (w, _) =>
            {
                var content = w.FindControl<ContentControl>("SettingsContent")!;
                var nav = w.FindControl<ListBox>("SettingsNav")!;
                nav.SelectedItem = nav.Items.Cast<object>().Single(item =>
                    item.ToString() == MessageCatalog.Get("common.models", InterfaceLanguage.Zh));
                Dispatcher.UIThread.RunJobs();
                await WaitForSettingsFrame(() => content.Content is StackPanel { Children.Count: > 0 }).ConfigureAwait(true);
                Assert.DoesNotContain("motion", w.Classes);
                Assert.Equal(1d, content.Opacity);
                Assert.Equal(0d, content.RenderTransform?.Value.M32 ?? 0);

                var key = "settings.context_maintenance";
                var link = w.FindControl<WrapPanel>("SettingsLinks")!.Children.OfType<Button>()
                    .First(button => Equals(button.Content, MessageCatalog.Get(key, InterfaceLanguage.Zh)));
                var group = w.FindControl<StackPanel>("SettingsBody")!.GetVisualDescendants()
                    .OfType<SettingsGroup>().First(settings => Equals(settings.Tag, key));
                var ring = group.FindControl<Border>("LandingRing")!;
                var scroller = w.FindControl<ScrollViewer>("SettingsScroller")!;
                link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.True(scroller.Offset.Y > 0);
                Assert.False(scroller.IsAnimating(ScrollViewer.OffsetProperty));
                Assert.False(ring.IsVisible);
            });

    /// <summary>Lets the dispatcher run due motion timers before checking the sampled state.</summary>
    /// <param name="ready">The sampled frame condition.</param>
    /// <returns>The wait for that state.</returns>
    private static async Task WaitForSettingsFrame(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!ready() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(4).ConfigureAwait(true);
        }

        Assert.True(ready(), "The animation did not reach the sampled state.");
    }
}
