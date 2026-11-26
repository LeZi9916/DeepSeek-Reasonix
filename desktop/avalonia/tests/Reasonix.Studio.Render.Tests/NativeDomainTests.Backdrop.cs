using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Shell.Appearance;
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
    /// <summary>Retains the current artwork until the replacement decode can commit.</summary>
    /// <returns>The controlled download and replacement.</returns>
    [Fact]
    public Task ThemeArtworkRemainsVisibleDuringReplacement() => WithSettingsAsync(
        "conversation",
        async (w, reads) =>
        {
            using var http = new HttpClient(reads, disposeHandler: false) { BaseAddress = new Uri("http://127.0.0.1:4000") };
            var api = new StudioPageClient(http);
            var backdrop = w.FindControl<ThemeBackdrop>("Backdrop")!;
            var image = backdrop.FindControl<Image>("Picture")!;
            var reply = new TaskCompletionSource<HttpResponseMessage?>();
            reads.Exchange = request => request.RequestUri!.AbsolutePath switch
            {
                "/themes/first/background" => Task.FromResult<HttpResponseMessage?>(Artwork()),
                "/themes/next/background" => reply.Task,
                _ => Task.FromResult<HttpResponseMessage?>(null),
            };

            await backdrop.ApplyAsync(Pack("first"), api, default).ConfigureAwait(true);
            var original = image.Source;
            Assert.NotNull(original);
            var loading = backdrop.ApplyAsync(Pack("next"), api, default);
            try
            {
                Assert.Same(original, image.Source);
            }
            finally
            {
                reply.TrySetResult(Artwork());
                await loading.ConfigureAwait(true);
            }

            Assert.NotNull(image.Source);
            Assert.NotSame(original, image.Source);

            static HttpResponseMessage Artwork() => new(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAUAAAADCAIAAADUVFKvAAAAE0lEQVR4nGNkYPjPgASYkDlY+AAtkwEF9+iIIgAAAABJRU5ErkJggg==")),
            };

            static JsonObject Pack(string id) => new()
            {
                ["id"] = id,
                ["background"] = new JsonObject { ["image"] = true },
            };
        });

    /// <summary>Checks an installed theme's asset, sky, busy opacity and return to the default.</summary>
    /// <returns>The visual interaction.</returns>
    [Fact]
    public Task InstalledThemeDrawsItsBackground() => WithSettings(
        "settings-appearance",
        (w, reads) =>
        {
            var packs = reads.Read<JsonArray>("/themes");
            var tide = packs.Single(p => (string?)p?["id"] == "tide")!;
            tide["background"] = new JsonObject { ["image"] = true, ["focusX"] = 0.25, ["focusY"] = 0.5, ["homeOpacity"] = 0.45, ["taskOpacity"] = 0.15 };
            reads.Exchange = async req =>
            {
                if (req.RequestUri!.AbsolutePath == "/themes/tide/background")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAUAAAADCAIAAADUVFKvAAAAE0lEQVR4nGNkYPjPgASYkDlY+AAtkwEF9+iIIgAAAABJRU5ErkJggg==")),
                    };
                }

                if (req.RequestUri.AbsolutePath != "/themes")
                {
                    return null;
                }

                if (req.Method == HttpMethod.Post)
                {
                    var id = (string?)JsonNode.Parse(await req.Content!.ReadAsStringAsync())?["id"];
                    foreach (var pack in packs)
                    {
                        pack!["active"] = (string?)pack["id"] == id;
                    }
                }

                return JsonReply(packs);
            };
            void Choose(string name)
            {
                var button = w.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "action:theme.activate" && AutomationProperties.GetName(b) == name);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
            }

            Choose("Tide");
            var art = w.GetVisualDescendants().OfType<Image>().Single(i => AutomationProperties.GetAutomationId(i) == "class:theme-background");
            Assert.True(art.Source is not null, w.FindControl<TextBlock>("SettingsError")!.Text);
            Assert.Equal(0.45, art.Opacity);
            w.ShowState(new Reasonix.Studio.State.SessionState { Running = true });
            Assert.Equal(0.15, art.Opacity);
            Choose(Reasonix.Studio.Pages.I18n.MessageCatalog.Get("common.default", Reasonix.Studio.Pages.I18n.InterfaceLanguage.Zh));
            Assert.Null(art.Source);
        });
}
