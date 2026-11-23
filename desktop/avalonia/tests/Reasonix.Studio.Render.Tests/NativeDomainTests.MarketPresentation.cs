using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks the listing and structured review surfaces against the old marketplace.</summary>
    /// <param name="width">Reference viewport width.</param>
    /// <param name="dark">Reference palette.</param>
    /// <returns>The marketplace presentation interactions.</returns>
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(760, false)]
    [InlineData(760, true)]
    public Task MarketPresentationKeepsTheReferenceDetailAndAcknowledgement(int width, bool dark) => WithSettings("conversation", (w, reads) =>
    {
        var height = width == 1440 ? 900 : 480;
        w.Width = width;
        w.Height = height;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var packages = reads.Read<JsonObject>("/market/packages")["packages"]!.AsArray();
        const string Single = "nanfei892/make-ui-not-ai";
        const string Many = "acme/review-kit";
        const string Unpinned = "1574022644/lm-studio-vision-bridge";
        const string Installed = "irmia/irmia-devkit";
        const string Digest = "sha256:5f0c1e9a7b3d2c4e6f8a0b1c2d3e4f5a6b7c8d9e0f1a2b3c4d5e6f7a8b9c0d1e";
        var applied = 0;
        var readPlan = 0;
        var votes = 0;
        var cached = false;
        var failRead = false;
        var failPreview = false;
        reads.Exchange = async req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("/vote", StringComparison.Ordinal))
            {
                if (req.Method == HttpMethod.Post)
                {
                    votes++;
                }

                var slug = path["/market/packages/".Length..^"/vote".Length];
                var package = Package(slug);
                return JsonReply(new JsonObject
                {
                    ["signedIn"] = true,
                    ["canVote"] = true,
                    ["value"] = slug == Many ? 1 : 0,
                    ["upCount"] = package["upCount"]?.DeepClone(),
                    ["downCount"] = package["downCount"]?.DeepClone(),
                    ["approvalRate"] = package["approvalRate"]?.DeepClone(),
                });
            }

            if (path.StartsWith("/market/packages/", StringComparison.Ordinal))
            {
                if (failRead)
                {
                    failRead = false;
                    return Unavailable("Fixture market read unavailable");
                }

                var slug = path["/market/packages/".Length..];
                var package = Package(slug).DeepClone().AsObject();
                var source = slug switch
                {
                    Single => "https://github.com/nanfei892/ship-it-skills/tree/3f1c2e7a9b0d4c6e8f1a2b3c4d5e6f7a8b9c0d1e/make-ui-not-ai",
                    Many => "https://github.com/acme/review-kit/tree/9c8b7a6f5e4d3c2b1a0f9e8d7c6b5a4f3e2d1c0b",
                    Unpinned => "https://github.com/FuchaZ/lm-studio-vision-bridge/blob/master/SKILL.md",
                    Installed => "irmia-devkit-mcp",
                    _ => (string?)package["repoUrl"],
                };
                var detail = new JsonObject
                {
                    ["package"] = package,
                    ["pinned"] = slug != Unpinned,
                    ["approved"] = new JsonObject { ["version"] = (string?)package["latestVersion"], ["source"] = source },
                };
                if (slug == Installed)
                {
                    detail["installed"] = new JsonObject { ["version"] = "2.7.0" };
                }

                if (cached)
                {
                    detail["cache"] = new JsonObject { ["cachedAt"] = "2026-10-05T08:30:00Z", ["cause"] = "unreachable" };
                }

                return JsonReply(detail);
            }

            if (path == "/market/plan")
            {
                readPlan++;
                if (failPreview)
                {
                    return Unavailable("Fixture preview unavailable");
                }

                var requestPlan = JsonNode.Parse(await req.Content!.ReadAsStringAsync().ConfigureAwait(false))!;
                var slug = (string)requestPlan["slug"]!;
                if (slug == Unpinned)
                {
                    Assert.True((bool?)requestPlan["trust"]);
                }

                var package = Package(slug);
                var names = slug == Many ? new[] { "review", "risk", "pr-notes" } : [(string)package["name"]!];
                var actions = names.Select(name =>
                {
                    var action = new JsonObject { ["kind"] = "skill", ["name"] = name, ["riskLevel"] = slug == Many ? "high" : "low" };
                    if (slug == Many)
                    {
                        action["riskReasons"] = new JsonArray("one source expands to 3 skills; every one of them is installed");
                    }

                    return action;
                });
                return JsonReply(new JsonObject
                {
                    ["ok"] = true,
                    ["version"] = (string?)package["latestVersion"],
                    ["planId"] = "fixture:" + slug,
                    ["unreviewed"] = slug == Unpinned,
                    ["contentDigest"] = Digest,
                    ["actions"] = new JsonArray(actions.Cast<JsonNode?>().ToArray()),
                });
            }

            if (path == "/market/install")
            {
                applied++;
            }

            return null;
        };

        Navigate(w, "settings-ext-market");
        Open(Single);
        Assert.Contains(w.GetVisualDescendants().OfType<TextBlock>(), item => item.IsEffectivelyVisible && item.Text == "社区市场");
        var detail = Named<Border>("ListingFrame");
        Assert.InRange(detail.Bounds.Width, width == 1440 ? 1151 : 471, width == 1440 ? 1153 : 473);
        Assert.Contains(detail.GetVisualDescendants().OfType<TextBlock>(), item => item.IsEffectivelyVisible && item.Text == "@nanfei892");
        Shot(detail, "pinned");

        ClickAt(w, "action:market.inspect");
        var singleReview = Named<Border>("ReviewFrame");
        Assert.InRange(singleReview.Bounds.Width, width == 1440 ? 559 : 471, width == 1440 ? 561 : 473);
        Assert.True(ControlAt<Button>(w, "action:market.install").IsEnabled);
        Assert.DoesNotContain(w.GetVisualDescendants().OfType<CheckBox>(), item => item.IsEffectivelyVisible && AutomationProperties.GetAutomationId(item) == "action:market.confirm-many");
        Shot(singleReview, "single");
        CloseReview();

        Open(Many);
        ClickAt(w, "action:market.inspect");
        var review = Named<Border>("ReviewFrame");
        Assert.Contains(review.GetVisualDescendants().OfType<TextBlock>(), item => item.IsEffectivelyVisible && item.Text == "risk");
        Assert.Contains(review.GetVisualDescendants().OfType<TextBlock>(), item => item.IsEffectivelyVisible && item.Text == "pr-notes");
        Assert.False(ControlAt<Button>(w, "action:market.install").IsEnabled);
        Shot(review, "many-unchecked", wide: true);
        Shot(ControlAt<Button>(w, "action:market.install"), "many-footer-unchecked", wide: true);
        ControlAt<CheckBox>(w, "action:market.confirm-many").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(ControlAt<Button>(w, "action:market.install").IsEnabled);
        Shot(review, "many-checked", wide: true);
        Shot(ControlAt<Button>(w, "action:market.install"), "many-footer-checked", wide: true);
        CloseReview();

        var pinned = ControlAt<CheckBox>(w, "action:market.pinned");
        pinned.IsChecked = false;
        PumpNavigation(() => w.GetVisualDescendants().OfType<Button>().Any(button => Equals(button.Tag, Unpinned)));
        Open(Unpinned);
        Assert.Equal(2, readPlan);
        Shot(Named<Border>("ListingFrame"), "unpinned");
        ClickAt(w, "action:market.trust");
        Assert.Equal(3, readPlan);
        Shot(Named<Border>("ReviewFrame"), "unreviewed");
        CloseReview();

        Open(Installed);
        Assert.DoesNotContain(w.GetVisualDescendants().OfType<Button>(), item => item.IsEffectivelyVisible && AutomationProperties.GetAutomationId(item) == "action:market.inspect");
        Shot(Named<Border>("ListingFrame"), "installed");
        ClickAt(w, "action:market.back");

        cached = true;
        Open(Single);
        Assert.False(ControlAt<Button>(w, "action:market.inspect").IsEnabled);
        Shot(Named<Border>("ListingFrame"), "cached");
        ClickAt(w, "action:market.back");

        cached = false;
        failRead = true;
        Open(Single);
        Assert.Contains("Fixture market read unavailable", Named<TextBlock>("StatusCopy").Text);
        Shot(Named<Border>("StatusBanner"), "read-error");
        ClickAt(w, "action:market.detail-retry");
        failPreview = true;
        ClickAt(w, "action:market.inspect");
        Assert.Contains("Fixture preview unavailable", Named<TextBlock>("DetailFailure").Text);
        Shot(Named<Border>("ListingFrame"), "preview-error");
        Assert.Equal(0, applied);
        Assert.Equal(0, votes);

        JsonObject Package(string slug) => packages.OfType<JsonObject>().Single(item => (string?)item["slug"] == slug);

        static HttpResponseMessage Unavailable(string message)
        {
            var response = JsonReply(new JsonObject { ["error"] = message });
            response.StatusCode = HttpStatusCode.ServiceUnavailable;
            return response;
        }

        void CloseReview()
        {
            ClickAt(w, "action:market.close-review");
            ClickAt(w, "action:market.back");
        }

        T Named<T>(string name)
            where T : Control => w.GetVisualDescendants().OfType<T>().Single(item => item.IsEffectivelyVisible && item.Name == name);

        void Open(string slug)
        {
            var item = w.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && Equals(button.Tag, slug)
                && AutomationProperties.GetAutomationId(button) == "action:market.open");
            item.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        void Shot(Control control, string phase, bool wide = false)
        {
            Align(control, wide);
            Capture(phase);
        }

        void Align(Control control, bool wide)
        {
            if (width != 760 && !wide)
            {
                return;
            }

            var scroll = w.FindControl<ScrollViewer>("SettingsScroller")!;
            var y = control.TranslatePoint(default, w)!.Value.Y;
            scroll.Offset = new Vector(0, Math.Max(0, scroll.Offset.Y + y - 120));
            w.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        void Capture(string phase) => CaptureFlow(w, $"native-market-detail-{phase}-{width}x{height}", resetScroll: false);
    });

    /// <summary>Verifies that pressing the existing marketplace vote withdraws it without changing the package.</summary>
    /// <returns>The native vote interaction using an in-memory host.</returns>
    [Fact]
    public Task MarketPresentationTogglesOnlyTheExplicitVote() => WithSettings("settings-ext-market", (w, reads) =>
    {
        var package = reads.Read<JsonObject>("/market/packages")["packages"]![0]!;
        var endpoint = "/market/packages/" + (string)package["slug"]!;
        var mine = 1;
        var writes = 0;
        reads.Exchange = async request =>
        {
            if (request.RequestUri!.AbsolutePath == endpoint)
            {
                return JsonReply(new JsonObject { ["package"] = package.DeepClone(), ["pinned"] = true });
            }

            if (request.RequestUri.AbsolutePath != endpoint + "/vote")
            {
                return null;
            }

            if (request.Method == HttpMethod.Post)
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false));
                mine = (int)body!["value"]!;
                writes++;
            }

            return JsonReply(new JsonObject
            {
                ["signedIn"] = true,
                ["canVote"] = true,
                ["value"] = mine,
                ["upCount"] = 40 + mine,
                ["downCount"] = 3,
                ["approvalRate"] = (40d + mine) / (43 + mine),
            });
        };

        ClickAt(w, "action:market.open");
        var vote = w.GetVisualDescendants().OfType<Button>().Single(item => item.IsEffectivelyVisible && Equals(item.Tag, "up")
            && AutomationProperties.GetAutomationId(item) == "action:market.vote");
        Assert.Contains("chosen", vote.Classes);
        Assert.Equal(0, writes);
        vote.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpNavigation(() => writes == 1 && !vote.Classes.Contains("chosen"));
        Assert.Equal(0, mine);
        vote.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpNavigation(() => writes == 2 && vote.Classes.Contains("chosen"));
        Assert.Equal(1, mine);
    });
}
