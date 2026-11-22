using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
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
