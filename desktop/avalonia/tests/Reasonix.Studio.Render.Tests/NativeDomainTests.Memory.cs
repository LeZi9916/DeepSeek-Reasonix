using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Events;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Tests editing, refusal, retry, history and restore from the data settings screen.</summary>
    /// <returns>The native interaction.</returns>
    [Fact]
    public Task MemoryEditsAndRestoresRevisions() => WithSettings(
        "settings-storage",
        (w, reads) =>
    {
        var catalog = reads.Read<JsonObject>("/memory");
        var item = catalog["memories"]!.AsArray()[0]!;
        var name = (string)item["name"]!;
        var original = item.DeepClone();
        var refuse = true;
        var saves = 0;
        var restores = 0;
        reads.Exchange = async request =>
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/memory":
                    return JsonReply(catalog);
                case "/memory/save":
                    var edit = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
                    Assert.Equal(name, (string?)edit["name"]);
                    Assert.Null(edit["path"]);
                    Assert.Null(edit["revision"]);
                    saves++;
                    if (refuse)
                    {
                        return new HttpResponseMessage(HttpStatusCode.Conflict);
                    }

                    item["body"] = (string?)edit["body"];
                    item["revision"] = 2;
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                case "/memory/revisions":
                    Assert.Equal("?name=" + name, request.RequestUri.Query);
                    return JsonReply(new JsonObject { ["revisions"] = new JsonArray(item.DeepClone(), original.DeepClone()) });
                case "/memory/restore":
                    var restore = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
                    Assert.Equal(name, (string?)restore["name"]);
                    Assert.Equal(1, (int?)restore["revision"]);
                    restores++;
                    item["body"] = (string?)original["body"];
                    item["revision"] = 3;
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                default:
                    return null;
            }
        };
        ControlAt<TextBox>(w, "action:memory.search").Text = name;
        Dispatcher.UIThread.RunJobs();
        ClickAt(w, "action:memory.edit");
        var body = ControlAt<TextBox>(w, "field:memory.body");
        body.Text = "discarded";
        ClickAt(w, "action:memory.cancel");
        Assert.Equal(0, saves);
        ClickAt(w, "action:memory.edit");
        Assert.Equal((string?)original["body"], body.Text);
        body.Text = "saved through the form";
        ClickAt(w, "action:memory.save");
        Assert.Equal(1, saves);
        Assert.True(body.IsEffectivelyVisible);
        Assert.Equal("saved through the form", body.Text);
        refuse = false;
        ClickAt(w, "action:memory.save");
        Assert.Equal(2, saves);
        Assert.False(body.IsEffectivelyVisible);
        ClickAt(w, "action:memory.history");
        ClickAt(w, "action:memory.restore");
        Assert.Equal(1, restores);
        ClickAt(w, "action:memory.edit");
        Assert.Equal((string?)original["body"], body.Text);
    });

    /// <summary>Runs a scenario on the UI thread through the main settings entry point.</summary>
    /// <param name="section">Baseline page name.</param>
    /// <param name="run">The actions in the mounted window.</param>
    /// <returns>The complete scenario.</returns>
    private static async Task WithSettings(string section, Action<MainWindow, ReferenceReads> run)
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(RasterBuilder));
        await session.Dispatch(
            () =>
        {
            using var reads = new ReferenceReads();
            using var http = new HttpClient(reads) { BaseAddress = new Uri("http://127.0.0.1:4000") };
            var engine = new SessionEngine(new KernelClient(http), new SseEventTransport(http.BaseAddress.ToString(), http));
            var window = new MainWindow(false, engine, InterfaceLanguage.Zh, _ => Task.FromResult(false));
            window.Show();
            try
            {
                Navigate(window, section);
                run(window, reads);
            }
            finally
            {
                window.Close();
            }
        },
            default);
    }

    /// <summary>Gets one visible action or field from the mounted page.</summary>
    /// <typeparam name="T">Native control type.</typeparam>
    /// <param name="w">Current window.</param>
    /// <param name="id">Stable action or field.</param>
    /// <returns>The control.</returns>
    private static T ControlAt<T>(MainWindow w, string id)
        where T : Control => w.GetVisualDescendants().OfType<T>().First(c => c.IsEffectivelyVisible && AutomationProperties.GetAutomationId(c) == id);

    /// <summary>Clicks an actual native action.</summary>
    /// <param name="w">Owning window.</param>
    /// <param name="id">Action id.</param>
    private static void ClickAt(MainWindow w, string id)
    {
        var control = ControlAt<Button>(w, id);
        Assert.True(control.IsEffectivelyEnabled, id);
        control.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Serializes a captured or updated endpoint result.</summary>
    /// <param name="node">Response body.</param>
    /// <returns>An HTTP success.</returns>
    private static HttpResponseMessage JsonReply(JsonNode node) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json"),
    };
}
