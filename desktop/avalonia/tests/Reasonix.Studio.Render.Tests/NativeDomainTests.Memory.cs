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
using Reasonix.Studio.Shell.Host;
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
        var confirmed = 0;
        var archived = 0;
        var policyWrites = 0;
        var chosenMode = "off";
        reads.Exchange = async request =>
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/memory/proactive":
                    if (request.Method == HttpMethod.Post)
                    {
                        var mode = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
                        chosenMode = (string)mode["mode"]!;
                        policyWrites++;
                        return new HttpResponseMessage(HttpStatusCode.NoContent);
                    }

                    return JsonReply(new JsonObject { ["mode"] = chosenMode });
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
                case "/memory/confirm":
                    Assert.Equal(name, (string?)JsonNode.Parse(await request.Content!.ReadAsStringAsync())?["name"]);
                    confirmed++;
                    item["new"] = false;
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                case "/memory/forget":
                    Assert.Equal(name, (string?)JsonNode.Parse(await request.Content!.ReadAsStringAsync())?["name"]);
                    archived++;
                    catalog["memories"]!.AsArray().Remove(item);
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
        var policy = w.GetVisualDescendants().OfType<RadioButton>().First(c => AutomationProperties.GetAutomationId(c) == "action:memory.proactive" && c.IsChecked != true);
        policy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, policyWrites);
        ControlAt<TextBox>(w, "action:memory.search").Text = name;
        Dispatcher.UIThread.RunJobs();
        ClickAt(w, "action:memory.edit");
        CaptureFlow(w, "memory-edit");
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
        CaptureFlow(w, "memory-history");
        ClickAt(w, "action:memory.restore");
        Assert.Equal(1, restores);
        ClickAt(w, "action:memory.edit");
        Assert.Equal((string?)original["body"], body.Text);
        ClickAt(w, "action:memory.cancel");
        ClickAt(w, "action:memory.confirm");
        Assert.Equal(1, confirmed);
        ClickAt(w, "action:memory.forget");
        Assert.Equal(1, archived);
        Assert.DoesNotContain(w.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && AutomationProperties.GetAutomationId(b) == "action:memory.open");
    });

    /// <summary>Reviews a held suggestion locally before saving the edited body.</summary>
    /// <returns>The native composer interaction.</returns>
    [Fact]
    public Task ComposerReviewsHeldMemory() => WithSettings(
        "conversation",
        (w, reads) =>
        {
            var accepts = 0;
            var dismisses = 0;
            var held = JsonNode.Parse("""{"suggestions":[{"id":"held","title":"A proposal","body":"original","scope":"project"},{"id":"other","body":"other"}]}""")!;
            reads.Exchange = async req =>
            {
                if (req.RequestUri!.AbsolutePath == "/memory/suggestions")
                {
                    return JsonReply(held);
                }

                if (req.RequestUri.AbsolutePath == "/memory/suggestions/accept")
                {
                    var edit = JsonNode.Parse(await req.Content!.ReadAsStringAsync())!;
                    Assert.Equal("held", (string?)edit["id"]);
                    Assert.Equal("approved wording", (string?)edit["body"]);
                    accepts++;
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }

                if (req.RequestUri.AbsolutePath == "/memory/suggestions/dismiss")
                {
                    dismisses++;
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }

                return null;
            };
            w.ShowState(new Reasonix.Studio.State.SessionState { Running = true });
            w.ShowState(new Reasonix.Studio.State.SessionState());
            Dispatcher.UIThread.RunJobs();
            ClickAt(w, "action:memory.suggest-edit");
            ControlAt<TextBox>(w, "action:memory.suggest-text").Text = "approved wording";
            Assert.Equal(0, accepts);
            ClickAt(w, "action:memory.suggest-save");
            ClickAt(w, "action:memory.suggest-dismiss");
            Assert.Equal(1, accepts);
            Assert.Equal(1, dismisses);
        });

    /// <summary>Checks the memory row pill and the reference label scale.</summary>
    /// <returns>The memory list style check.</returns>
    [Fact]
    public Task MemoryRowsUseTheReferencePillAndTypeScale() => WithSettings("settings-storage", (w, _) =>
    {
        var entries = w.GetVisualDescendants().OfType<Button>()
            .Where(button => AutomationProperties.GetAutomationId(button) == "action:memory.open" && button.IsEffectivelyVisible)
            .ToArray();
        Assert.NotEmpty(entries);
        var badge = entries.SelectMany(entry => entry.GetVisualDescendants().OfType<Border>())
            .FirstOrDefault(border => border.Child is TextBlock { Text: "新" });
        Assert.NotNull(badge);
        Assert.Equal(999, badge.CornerRadius.TopLeft);
        Assert.NotNull(badge.Background);
        var title = entries[0].GetVisualDescendants().OfType<TextBlock>().First(block => block.Text == "每天最多发一个版本");
        Assert.Equal(13, title.FontSize);
        var metadata = entries[0].GetVisualDescendants().OfType<TextBlock>().First(block => block.Text?.Contains(" · ", StringComparison.Ordinal) == true);
        Assert.Equal(10, metadata.FontSize);
    });

    /// <summary>Opens the reference composer with the optional mode picker enabled.</summary>
    /// <param name="run">The mounted composer interactions.</param>
    /// <returns>The complete scenario.</returns>
    private static Task WithComposerPicker(Action<MainWindow, ReferenceReads> run) =>
        WithSettings("conversation", run, new WorkspacePreferences { ShowModePicker = true });

    /// <summary>Runs a scenario on the UI thread through the main settings entry point.</summary>
    /// <param name="section">Baseline page name.</param>
    /// <param name="run">The actions in the mounted window.</param>
    /// <param name="workspacePreferences">The native window's saved device options, when already configured.</param>
    /// <returns>The complete scenario.</returns>
    private static async Task WithSettings(string section, Action<MainWindow, ReferenceReads> run, WorkspacePreferences? workspacePreferences = null)
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(RasterBuilder));
        await session.Dispatch(
            () =>
        {
            using var reads = new ReferenceReads();
            using var http = new HttpClient(reads) { BaseAddress = new Uri("http://127.0.0.1:4000") };
            var engine = new SessionEngine(new KernelClient(http), new SseEventTransport(http.BaseAddress.ToString(), http));
            var window = new MainWindow(false, engine, InterfaceLanguage.Zh, _ => Task.FromResult(false), workspacePreferences: workspacePreferences) { Width = 1440, Height = 900, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light };
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

    /// <summary>Renders the settings state reached through the ordinary controls.</summary>
    /// <param name="window">The active window.</param>
    /// <param name="scene">The editor state.</param>
    /// <param name="resetScroll">Whether to capture the page from its beginning.</param>
    /// <param name="region">A popup region when it is rendered in a separate layer.</param>
    private static void CaptureFlow(MainWindow window, string scene, bool resetScroll = true, Control? region = null)
    {
        var output = Environment.GetEnvironmentVariable("STUDIO_PARITY_OUTPUT");
        if (string.IsNullOrEmpty(output))
        {
            return;
        }

        if (resetScroll)
        {
            window.FindControl<ScrollViewer>("SettingsScroller")!.Offset = default;
        }

        Dispatcher.UIThread.RunJobs();
        if (scene == "memory-history")
        {
            ControlAt<Button>(window, "action:memory.restore").BringIntoView();
            Dispatcher.UIThread.RunJobs();
        }

        System.IO.Directory.CreateDirectory(output);
        using var image = new Avalonia.Media.Imaging.RenderTargetBitmap(Avalonia.PixelSize.FromSize(region?.Bounds.Size ?? window.ClientSize, 1), new Avalonia.Vector(96, 96));
        image.Render(region ?? window);
        var scheme = window.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? "dark" : "light";
        image.Save(System.IO.Path.Combine(output, scene + "." + scheme + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
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
