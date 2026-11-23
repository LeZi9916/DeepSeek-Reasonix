using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks the discovery filters against the host catalog and the reference card sizes.</summary>
    /// <param name="width">Reference viewport width.</param>
    /// <param name="dark">Reference color scheme.</param>
    /// <returns>The marketplace interactions.</returns>
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(760, false)]
    [InlineData(760, true)]
    public Task ExtensionMarketplaceFiltersAndKeepsTheReferenceCards(int width, bool dark) => WithSettings("conversation", (w, reads) =>
    {
        var height = width == 1440 ? 900 : 480;
        w.Width = width;
        w.Height = height;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var page = reads.Read<JsonObject>("/market/packages");
        var requested = string.Empty;
        reads.Exchange = request =>
        {
            if (request.RequestUri!.AbsolutePath != "/market/packages")
            {
                return Task.FromResult<HttpResponseMessage?>(null);
            }

            requested = request.RequestUri.Query;
            var packages = page["packages"]!.AsArray().OfType<JsonObject>();
            if (requested.Contains("kind=plugin", StringComparison.Ordinal) || requested.Contains("q=review", StringComparison.Ordinal))
            {
                packages = packages.Where(item => (string?)item["kind"] == "plugin");
            }

            if (requested.Contains("q=missing-fixture", StringComparison.Ordinal))
            {
                packages = [];
            }

            if (requested.Contains("pinned=1", StringComparison.Ordinal))
            {
                packages = packages.Where(item => (bool?)item["pinned"] == true);
            }

            return Task.FromResult<HttpResponseMessage?>(JsonReply(new JsonObject { ["packages"] = new JsonArray(packages.Select(item => item.DeepClone()).ToArray()), ["limit"] = 24 }));
        };

        Navigate(w, "settings-ext-market");
        var body = w.FindControl<ContentControl>("SettingsContent")!;
        var query = ControlAt<TextBox>(w, "action:market.search");
        var pinned = ControlAt<CheckBox>(w, "action:market.pinned");
        Assert.True(pinned.IsChecked);
        PumpNavigation(() => Cards().Length == 4);
        Assert.Equal(width == 1440 ? 279 : 472, Cards()[0].Bounds.Width);
        Assert.Contains("pinned=1", requested, StringComparison.Ordinal);
        Capture("market");
        if (width == 760)
        {
            Align(query, 120);
        }

        Capture("market-list");
        if (width == 760)
        {
            Align(Cards()[0], 120);
        }

        Capture("market-cards");
        Click("action:market.kind", "plugin");
        PumpNavigation(() => requested.Contains("kind=plugin", StringComparison.Ordinal) && Cards().Length == 1);
        Capture("market-filter");
        Click("action:market.kind", string.Empty);
        query.Text = "review";
        PumpNavigation(() => requested.Contains("q=review", StringComparison.Ordinal) && Cards().Length == 1);
        Capture("market-search");
        query.Text = "missing-fixture";
        PumpNavigation(() => requested.Contains("q=missing-fixture", StringComparison.Ordinal) && Cards().Length == 0);
        Capture("market-empty");
        query.Text = string.Empty;
        pinned.IsChecked = false;
        PumpNavigation(() => !requested.Contains("pinned=1", StringComparison.Ordinal) && Cards().Length > 4);
        Capture("market-unpinned");

        Button[] Cards() => body.GetVisualDescendants().OfType<Button>().Where(button =>
            button.IsEffectivelyVisible && AutomationProperties.GetAutomationId(button) == "action:market.open").ToArray();

        void Click(string id, string tag)
        {
            var target = body.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == id && Equals(button.Tag, tag));
            target.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        void Align(Control control, double y)
        {
            var scroll = w.FindControl<ScrollViewer>("SettingsScroller")!;
            var at = control.TranslatePoint(default, w)!.Value.Y;
            scroll.Offset = new Avalonia.Vector(0, Math.Max(0, scroll.Offset.Y + at - y));
            Dispatcher.UIThread.RunJobs();
        }

        void Capture(string phase) => CaptureFlow(w, $"native-extensions-{phase}-{width}x{height}", resetScroll: false);
    });
}
