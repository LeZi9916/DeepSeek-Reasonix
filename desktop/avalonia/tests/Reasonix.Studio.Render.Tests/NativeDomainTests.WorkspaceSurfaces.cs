using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Workspace;
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
    /// <summary>Search scopes preserve canonical session actions and keyboard selection.</summary>
    /// <returns>The native palette interaction.</returns>
    [Fact]
    public Task WorkspacePaletteScopesAndKeysDispatchSelectedTarget() => WithSettings("conversation", (w, _) =>
    {
        var picked = string.Empty;
        var panel = new WorkspaceSearchPanel();
        Assert.IsType<Grid>(w.FindControl<LayoutTransformControl>("ScaledWindow")!.Child).Children.Add(panel);
        panel.Open(
            new[]
            {
                new WorkspaceSearchHit("actions", "New session", "Alt N", () => picked = "new"),
                new WorkspaceSearchHit("sessions", "Remote answer", "gpu-box · project", () => picked = "remote"),
                new WorkspaceSearchHit("settings", "Appearance", "appearance", () => picked = "appearance"),
            },
            InterfaceLanguage.En);
        Dispatcher.UIThread.RunJobs();
        var query = panel.FindControl<TextBox>("Query")!;
        var results = panel.FindControl<ListBox>("Results")!;
        query.Text = "#gpu-box";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Remote answer", Assert.Single(results.Items.OfType<WorkspaceSearchHit>(), hit => hit.Selectable).Label);
        query.Focus();
        w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        w.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("remote", picked);
        Assert.False(panel.IsVisible);
        panel.Open(new[] { new WorkspaceSearchHit("settings", "Appearance", "appearance", () => picked = "appearance") }, InterfaceLanguage.En);
        query.Text = ",appearance";
        Dispatcher.UIThread.RunJobs();
        Assert.Single(results.Items.OfType<WorkspaceSearchHit>(), hit => hit.Selectable);
        query.Text = "absent";
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(results.Items);
        Assert.True(panel.FindControl<TextBlock>("Empty")!.IsVisible);
        panel.Hide();
    });

    /// <summary>Device access opens only on user action and draws the actual hub offer.</summary>
    /// <returns>The acknowledged local-network card.</returns>
    [Fact]
    public Task DeviceAccessUsesCanonicalStatusAndExplicitToggle() => WithSettings("conversation", (w, reads) =>
    {
        var opened = false;
        var offers = 0;
        var refuse = true;
        JsonObject Status()
        {
            var status = JsonNode.Parse("""{"addresses":[{"ip":"192.168.1.10","kind":"lan"}],"devices":[],"cloudDevices":[],"cloudRemote":{"online":false}}""")!.AsObject();
            status["open"] = opened;
            status["offerExpires"] = offers > 0 ? "2026-11-22T15:00:00Z" : null;
            return status;
        }

        reads.Exchange = request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/share")
            {
                return Task.FromResult<HttpResponseMessage?>(JsonReply(Status()));
            }

            if (path == "/share/open")
            {
                if (refuse)
                {
                    return Task.FromResult<HttpResponseMessage?>(new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("{}") });
                }

                opened = true;
                var status = Status();
                status["open"] = true;
                return Task.FromResult<HttpResponseMessage?>(JsonReply(status));
            }

            if (path == "/share/offer")
            {
                offers++;
                return Task.FromResult<HttpResponseMessage?>(JsonReply(new JsonObject
                {
                    ["url"] = "http://192.168.1.10/pair#canonical",
                    ["qr"] = "<svg viewBox='0 0 10 10'><rect x='0' y='0' width='10' height='10' fill='white'/><path d='M1 1h3v3H1z' fill='black'/></svg>",
                    ["expires"] = "2026-11-22T15:00:00Z",
                }));
            }

            return Task.FromResult<HttpResponseMessage?>(null);
        };
        using var http = new HttpClient(reads) { BaseAddress = new Uri("http://127.0.0.1:4000") };
        var card = new DeviceAccessCard();
        Assert.IsType<Grid>(w.FindControl<LayoutTransformControl>("ScaledWindow")!.Child).Children.Add(card);
        card.OpenAsync(new StudioPageClient(http), InterfaceLanguage.Zh).GetAwaiter().GetResult();
        Assert.False(opened);
        Assert.Equal(0, offers);
        var toggle = card.FindControl<Avalonia.Controls.Primitives.ToggleButton>("AllowLocal")!;
        toggle.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.False(opened);
        Assert.False(toggle.IsChecked);
        Assert.True(card.FindControl<TextBlock>("Failure")!.IsVisible);
        refuse = false;
        toggle.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(opened);
        Assert.True(toggle.IsChecked);
        Assert.Equal(1, offers);
        Assert.True(card.FindControl<Grid>("LocalCode")!.IsVisible);
    });
}
