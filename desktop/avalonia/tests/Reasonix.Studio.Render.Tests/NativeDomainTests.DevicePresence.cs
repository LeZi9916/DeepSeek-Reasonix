using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Workspace;
using System;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>The hub withdrawing a spent pairing offer removes its QR and exposes an explicit new-code action.</summary>
    /// <returns>The acknowledged offer and subsequent canonical withdrawal.</returns>
    [Fact]
    public Task DeviceAccessWithdrawsSpentPairingOffer() => WithSettings("conversation", (w, reads) =>
    {
        var spent = false;
        var offered = false;
        JsonObject Status() => new()
        {
            ["open"] = true,
            ["addresses"] = new JsonArray(new JsonObject { ["ip"] = "127.0.0.1" }),
            ["devices"] = new JsonArray(),
            ["cloudDevices"] = new JsonArray(),
            ["offerExpires"] = offered && !spent ? "2026-11-22T18:00:00Z" : null,
        };
        reads.Exchange = request =>
        {
            if (request.RequestUri!.AbsolutePath == "/share/offer")
            {
                offered = true;
                return Task.FromResult<HttpResponseMessage?>(JsonReply(new JsonObject { ["url"] = "http://127.0.0.1/pair#local", ["qr"] = "<svg viewBox='0 0 10 10'><rect width='10' height='10' fill='white'/></svg>", ["expires"] = "2026-11-22T18:00:00Z" }));
            }

            return Task.FromResult<HttpResponseMessage?>(request.RequestUri.AbsolutePath == "/share" ? JsonReply(Status()) : null);
        };
        using var http = new HttpClient(reads) { BaseAddress = new Uri("http://127.0.0.1:4000") };
        var card = new DeviceAccessCard();
        Assert.IsType<Grid>(w.FindControl<LayoutTransformControl>("ScaledWindow")!.Child).Children.Add(card);
        card.OpenAsync(new StudioPageClient(http), InterfaceLanguage.En).GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
        Assert.True(card.FindControl<Grid>("LocalCode")!.IsVisible);
        spent = true;
        card.ShowStatus(Status());
        Dispatcher.UIThread.RunJobs();
        Assert.False(card.FindControl<Grid>("LocalCode")!.IsVisible);
        Assert.True(card.FindControl<Button>("MintLocal")!.IsVisible);
    });

    /// <summary>Canonical online devices update the badge and show one presence note after the initial read.</summary>
    /// <returns>The visible native connection notification.</returns>
    [Fact]
    public Task WorkspaceChromePresenceFollowsCanonicalDeviceChanges() => WithSettings("conversation", (w, _) =>
    {
        var chrome = w.FindControl<WorkspaceChromeStatus>("WorkspaceStatus")!;
        var status = new JsonObject { ["devices"] = new JsonArray(), ["cloudDevices"] = new JsonArray() };
        chrome.ShowDevices(status, InterfaceLanguage.En);
        var presence = Assert.IsType<Popup>(chrome.FindControl<Popup>("Presence"));
        Assert.False(presence.IsOpen);
        status["cloudDevices"] = new JsonArray(new JsonObject { ["id"] = "cloud-device", ["name"] = "Web Studio", ["ordinal"] = 1 });
        chrome.ShowDevices(status, InterfaceLanguage.En);
        Dispatcher.UIThread.RunJobs();
        Assert.True(presence.IsOpen);
        Assert.Equal("1", chrome.FindControl<TextBlock>("OnlineCount")!.Text);
        Assert.Equal(MessageCatalog.Get("remote.web_studio_connected", InterfaceLanguage.En), chrome.FindControl<TextBlock>("PresenceText")!.Text);
        status["cloudDevices"] = new JsonArray();
        chrome.ShowDevices(status, InterfaceLanguage.En, true);
        Assert.False(presence.IsOpen);
    });
}
