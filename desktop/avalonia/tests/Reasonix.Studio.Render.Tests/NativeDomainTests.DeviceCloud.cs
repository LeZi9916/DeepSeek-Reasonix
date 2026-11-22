using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Workspace;
using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Cloud controllers share the canonical device list and two-step revoke acknowledgement.</summary>
    /// <returns>The visible internet device and actual revoke request.</returns>
    [Fact]
    public Task DeviceAccessCloudListUsesCanonicalRevoke() => WithSettings("conversation", (w, reads) =>
    {
        var revoked = string.Empty;
        var devices = new JsonArray(new JsonObject { ["id"] = "cloud-a", ["name"] = "Web Studio", ["ordinal"] = 2 });
        JsonObject Status() => new()
        {
            ["open"] = false,
            ["addresses"] = new JsonArray(),
            ["devices"] = new JsonArray(),
            ["cloudDevices"] = devices.DeepClone(),
            ["cloudRemote"] = new JsonObject { ["online"] = false },
        };
        reads.Exchange = async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/share/revoke")
            {
                revoked = (string)JsonNode.Parse(await request.Content!.ReadAsStringAsync())!["id"]!;
                devices.Clear();
                return JsonReply(Status());
            }

            return request.RequestUri.AbsolutePath == "/share" ? JsonReply(Status()) : null;
        };
        using var http = new HttpClient(reads) { BaseAddress = new Uri("http://127.0.0.1:4000") };
        var card = new DeviceAccessCard();
        Assert.IsType<Grid>(w.FindControl<LayoutTransformControl>("ScaledWindow")!.Child).Children.Add(card);
        card.OpenAsync(new StudioPageClient(http), InterfaceLanguage.En).GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("Web Studio", StringComparison.Ordinal) == true);
        var revoke = card.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:share.ask-revoke");
        revoke.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(string.Empty, revoked);
        revoke.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("cloud-a", revoked);
        Assert.DoesNotContain(card.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetAutomationId(button) == "action:share.ask-revoke");
    });
}
