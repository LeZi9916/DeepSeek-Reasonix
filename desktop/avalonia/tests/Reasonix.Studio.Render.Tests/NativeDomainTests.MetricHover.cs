using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Kernel.Client;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Samples both directions of the real composer metric hover transition.</summary>
    /// <param name="name">The metric card and trigger prefix.</param>
    /// <param name="hiddenScale">The collapsed scale at zero opacity.</param>
    /// <returns>The mounted hover motion check.</returns>
    [Theory]
    [InlineData("Speed", 1.0)]
    [InlineData("Cache", 0.985)]
    [InlineData("Context", 0.985)]
    public Task ComposerMetricHoverFadesWithoutJumpingOnReentry(string name, double hiddenScale) => WithSettingsAsync(
        "conversation",
        async (w, reads) =>
        {
            await WaitForMotionFrame(() => w.Classes.Contains("motion")).ConfigureAwait(true);
            w.AdoptComposer(reads.Read<SessionStatus>("/status"));
            w.UpdateLayout();
            var hover = w.FindControl<StudioHoverCard>(name + "Hover")!;
            hover.Resources["TLine"] = TimeSpan.FromMilliseconds(360);
            var trigger = w.FindControl<Button>(name + "Toggle")!;
            var frame = hover.FindControl<Border>("Frame")!;
            var popup = hover.FindControl<Popup>("Surface")!;
            var inside = trigger.TranslatePoint(new Point(10, 10), w)!.Value;
            var outside = new Point(600, 100);
            var stem = "metric-hover-" + name.ToLowerInvariant();

            w.MouseMove(outside);
            w.MouseMove(inside);
            Assert.True(hover.IsOpen);
            await WaitForMotionFrame(() => frame.Opacity is > 0.37 and < 0.92).ConfigureAwait(true);
            AssertPose();
            Capture("enter");
            await WaitForMotionFrame(() => frame.Opacity == 1 && !frame.IsAnimating(Visual.OpacityProperty)).ConfigureAwait(true);
            Capture("open");

            w.MouseMove(outside);
            await WaitForMotionFrame(() => !hover.IsOpen && InFlight()).ConfigureAwait(true);
            Assert.True(popup.IsOpen);
            Assert.False(frame.IsHitTestVisible);
            AssertPose();
            Capture("exit");
            var held = frame.Opacity;
            hover.Open();
            Assert.True(hover.IsOpen);
            Assert.True(frame.IsHitTestVisible);
            Assert.Equal(held, frame.Opacity, 5);
            AssertPose();

            w.Classes.Remove("motion");
            Assert.Equal(1, frame.Opacity);
            AssertPose();
            w.Classes.Add("motion");
            w.MouseMove(inside);
            w.MouseMove(outside);
            await WaitForMotionFrame(() => !popup.IsOpen).ConfigureAwait(true);
            Capture("closed");

            bool InFlight() => frame.Opacity is > 0.12 and < 0.67;

            void AssertPose()
            {
                var transform = frame.RenderTransform!.Value;
                Assert.Equal(4 * (1 - frame.Opacity), transform.M32, 5);
                Assert.Equal(double.Lerp(hiddenScale, 1, frame.Opacity), transform.M11, 5);
            }

            void Capture(string phase)
            {
                var top = (int)Math.Ceiling(frame.Bounds.Height + hover.Gap + 8);
                CaptureMotionRegion(w, trigger, -8, top, (int)Math.Ceiling(frame.Bounds.Width) + 16, top + 29, $"{stem}-{phase}");
            }
        },
        allowMotion: true);

    /// <summary>Checks that the native popup preserves the last painted opacity until it closes.</summary>
    /// <param name="name">The metric card and trigger prefix.</param>
    /// <returns>The opacity samples captured across the two hover transitions.</returns>
    [Theory]
    [InlineData("Speed")]
    [InlineData("Cache")]
    [InlineData("Context")]
    public Task ComposerMetricHoverDoesNotFlashAtFadeEndpoints(string name) => WithSettingsAsync(
        "conversation",
        async (window, reads) =>
        {
            await WaitForMotionFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
            window.AdoptComposer(reads.Read<SessionStatus>("/status"));
            window.UpdateLayout();
            var hover = window.FindControl<StudioHoverCard>(name + "Hover")!;
            hover.Resources["TLine"] = TimeSpan.FromMilliseconds(240);
            var trigger = window.FindControl<Button>(name + "Toggle")!;
            var frame = hover.FindControl<Border>("Frame")!;
            var popup = hover.FindControl<Popup>("Surface")!;
            var inside = trigger.TranslatePoint(new Point(10, 10), window)!.Value;
            var outside = new Point(600, 100);
            var samples = new System.Collections.Generic.List<double>();
            frame.PropertyChanged += (_, change) =>
            {
                if (change.Property == Visual.OpacityProperty && popup.IsOpen)
                {
                    samples.Add(frame.Opacity);
                }
            };

            window.MouseMove(outside);
            window.MouseMove(inside);
            await WaitForMotionFrame(() => frame.Opacity == 1 && !frame.IsAnimating(Visual.OpacityProperty)).ConfigureAwait(true);
            Assert.Contains(samples, value => value is > 0 and < 1);
            Assert.Equal(samples.Order().ToArray(), samples.ToArray());

            samples.Clear();
            window.MouseMove(outside);
            await WaitForMotionFrame(() => !popup.IsOpen).ConfigureAwait(true);
            Assert.Contains(samples, value => value is > 0 and < 1);
            Assert.Equal(samples.OrderDescending().ToArray(), samples.ToArray());
        },
        allowMotion: true);

    /// <summary>A session with no billed requests does not acquire an empty cost chip.</summary>
    /// <returns>The initial ledger reading.</returns>
    [Fact]
    public Task EmptyLedgerDoesNotShowAnInapplicableSessionCost() => WithSettings("conversation", (w, reads) =>
    {
        var quote = System.Text.Json.JsonSerializer.Deserialize<Reasonix.Studio.Contracts.Wire.CostQuote>("{\"coverage\":\"none\"}", System.Text.Json.JsonSerializerOptions.Web)!;
        w.AdoptComposer(reads.Read<SessionStatus>("/status") with
        {
            SessionCostQuote = quote,
        });
        w.ShowState(new Reasonix.Studio.State.SessionState());
        Assert.False(w.FindControl<TextBlock>("SessionCostText")!.IsVisible);
    });

    /// <summary>An authoritative unknown quote cannot revive an older exact streaming total.</summary>
    /// <returns>The supported unknown amount reading.</returns>
    [Fact]
    public Task UnknownLedgerQuoteDoesNotReviveAnOlderExactAmount() => WithSettings("conversation", (w, reads) =>
    {
        var quote = System.Text.Json.JsonSerializer.Deserialize<Reasonix.Studio.Contracts.Wire.CostQuote>("{\"coverage\":\"incomplete\"}", System.Text.Json.JsonSerializerOptions.Web)!;
        w.AdoptComposer(reads.Read<SessionStatus>("/status") with
        {
            SessionCostQuote = quote,
        });
        w.ShowState(new Reasonix.Studio.State.SessionState
        {
            Metrics = new Reasonix.Studio.State.Metrics { Cost = new[] { new Reasonix.Studio.State.CurrencyAmount("CNY", 9.99) } },
        });
        Assert.EndsWith(" —", w.FindControl<TextBlock>("SessionCostText")!.Text);
    });

    /// <summary>Reopened sessions display covered ledger totals without requiring a new live usage event.</summary>
    /// <returns>The canonical ledger and masking interaction.</returns>
    [Fact]
    public Task ComposerMetricsRestoreLedgerCostsAndExcludeUnsplitCacheTokens() => WithSettings("conversation", (w, reads) =>
    {
        var quote = System.Text.Json.JsonSerializer.Deserialize<Reasonix.Studio.Contracts.Wire.CostQuote>("{\"coverage\":\"partial\",\"selected\":{\"currency\":\"CNY\",\"amount\":\"1.20\"}}", System.Text.Json.JsonSerializerOptions.Web)!;
        w.AdoptComposer(reads.Read<SessionStatus>("/status") with
        {
            SessionUsage = new SessionUsage { Requests = 12, CacheHit = 900, CacheMiss = 300, CacheUnknown = 200 },
            SessionCostQuote = quote,
        });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("90%", w.FindControl<TextBlock>("CacheText")!.Text);
        Assert.True(w.FindControl<TextBlock>("SessionCostText")!.IsVisible);
        Assert.Contains("≥", w.FindControl<TextBlock>("SessionCostText")!.Text);
        Assert.Contains("1.20", w.FindControl<TextBlock>("SessionCostText")!.Text);
        w.FindControl<Button>("HideAmounts")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.DoesNotContain("1.20", w.FindControl<TextBlock>("SessionCostText")!.Text);
    });

    /// <summary>The balance action opens spending and usage rather than the data settings home.</summary>
    /// <returns>The actual settings navigation.</returns>
    [Fact]
    public Task WalletOpensTheUsageDestination() => WithSettings("conversation", (w, _) =>
    {
        w.FindControl<Button>("WalletButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Reasonix.Studio.Pages.I18n.MessageCatalog.Get("app.usage", Reasonix.Studio.Pages.I18n.InterfaceLanguage.Zh), w.FindControl<TextBlock>("SettingsHeading")!.Text);
        Assert.NotNull(ControlAt<Button>(w, "action:usage.export"));
    });

    /// <summary>Pointer and keyboard input open the real metric cards and dismiss pinned details.</summary>
    /// <returns>The mounted metric interaction.</returns>
    [Fact]
    public Task ComposerMetricsUseHoverCardsAndKeepPointerAccessInsideDetails() => WithSettings("conversation", (w, reads) =>
    {
        w.AdoptComposer(reads.Read<SessionStatus>("/status"));
        Dispatcher.UIThread.RunJobs();
        var speed = w.FindControl<StudioHoverCard>("SpeedHover")!;
        var cache = w.FindControl<StudioHoverCard>("CacheHover")!;
        var context = w.FindControl<StudioHoverCard>("ContextHover")!;
        var anchor = w.FindControl<Button>("SpeedToggle")!;
        w.MouseMove(anchor.TranslatePoint(new Point(10, 10), w)!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.True(speed.IsOpen);
        w.MouseMove(speed.Card!.TranslatePoint(new Point(20, 20), w)!.Value);
        Thread.Sleep(140);
        Dispatcher.UIThread.RunJobs();
        Assert.True(speed.IsOpen);
        NativeClick("CacheToggle");
        Dispatcher.UIThread.RunJobs();
        Assert.True(cache.IsPinned);
        NativeClick("ContextToggle");
        Dispatcher.UIThread.RunJobs();
        Assert.False(cache.IsOpen);
        Assert.True(context.IsOpen);
        Assert.False(w.FindControl<Control>("ContextPanel")!.IsVisible);
        var details = w.FindControl<Reasonix.Studio.Shell.Metrics.ContextSummaryCard>("ContextDetails")!;
        var cacheButton = w.FindControl<Button>("CacheToggle")!;
        w.MouseMove(cacheButton.TranslatePoint(new Point(10, 10), w)!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.False(cache.IsOpen);
        ClickControl(details.FindControl<StackPanel>("Parts")!.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:context.memory-files"));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(details.FindControl<StackPanel>("Details")!.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "REASONIX.md");
        w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        w.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(context.IsOpen);
        NativeClick("ContextToggle");
        Assert.True(context.IsPinned);
        NativeClick("ContextToggle");
        Assert.False(context.IsOpen);
        NativeClick("ContextToggle");
        w.MouseMove(new Point(600, 100));
        w.MouseDown(new Point(600, 100), MouseButton.Left);
        w.MouseUp(new Point(600, 100), MouseButton.Left);
        Assert.False(context.IsOpen);
        Thread.Sleep(140);
        Dispatcher.UIThread.RunJobs();
        Assert.False(speed.IsOpen);

        void NativeClick(string name) => ClickControl(w.FindControl<Button>(name)!);

        void ClickControl(Control button)
        {
            var point = button.TranslatePoint(new Point(10, 10), w)!.Value;
            w.MouseMove(point);
            w.MouseDown(point, MouseButton.Left);
            w.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
    });
}
