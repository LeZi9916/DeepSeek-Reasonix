using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Creates, duplicates and edits a versioned preset through the list and editor.</summary>
    /// <returns>The native interaction.</returns>
    [Fact]
    public Task PresetsCreateCopyAndRespectRevisions() => WithSettings(
        "settings-session",
        (w, reads) =>
        {
            var catalog = reads.Read<JsonObject>("/agent-presets");
            var versions = catalog["presets"]!.AsArray();
            var source = versions[0]!["definition"]!.DeepClone();
            var count = 0;
            var refuse = false;
            JsonObject? submitted = null;
            var expected = string.Empty;
            reads.Exchange = async request =>
            {
                if (request.RequestUri!.AbsolutePath != "/agent-presets")
                {
                    return null;
                }

                if (request.Method == HttpMethod.Get)
                {
                    return JsonReply(catalog);
                }

                Assert.Equal(HttpMethod.Post, request.Method);
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
                submitted = body["definition"]!.AsObject();
                expected = (string)body["expectedRevision"]!;
                count++;
                if (refuse)
                {
                    return new HttpResponseMessage(HttpStatusCode.Conflict);
                }

                var version = new JsonObject { ["definition"] = submitted.DeepClone(), ["revision"] = "version-" + count };
                var old = versions.FirstOrDefault(v => (string?)v?["definition"]?["id"] == (string?)submitted["id"]);
                if (old is not null)
                {
                    versions.Remove(old);
                }

                versions.Add(version.DeepClone());
                return JsonReply(version);
            };
            ClickAt(w, "action:agent-preset.create");
            var name = ControlAt<TextBox>(w, "field:agent-preset.name");
            Assert.False(ControlAt<Button>(w, "action:agent-preset.save").IsEnabled);
            name.Text = "personal review";
            var id = ControlAt<TextBox>(w, "field:agent-preset.id");
            id.Text = "bad id";
            Assert.False(ControlAt<Button>(w, "action:agent-preset.save").IsEnabled);
            id.Text = "custom";
            ControlAt<TextBox>(w, "field:persona.suffix").Text = "Keep these instructions.";
            ClickAt(w, "action:agent-preset.save");
            Assert.Equal("user:custom", (string?)submitted!["id"]);
            Assert.Equal(string.Empty, expected);
            Assert.Equal("Keep these instructions.", (string?)submitted["persona"]?["suffix"]);
            Assert.True(JsonNode.DeepEquals(source["tools"], submitted["tools"]));
            ClickOn("action:agent-preset.copy", "user:custom");
            Assert.Equal("custom-1", ControlAt<TextBox>(w, "field:agent-preset.id").Text);
            Assert.Equal("Keep these instructions.", ControlAt<TextBox>(w, "field:persona.suffix").Text);
            name.Text = "review copy";
            refuse = true;
            ClickAt(w, "action:agent-preset.save");
            Assert.Equal(2, count);
            Assert.True(name.IsEffectivelyVisible);
            Assert.Equal("review copy", name.Text);
            refuse = false;
            ClickAt(w, "action:agent-preset.save");
            Assert.Equal("user:custom-1", (string?)submitted["id"]);
            Assert.Equal(string.Empty, expected);
            ClickOn("action:agent-preset.view", "user:custom-1");
            Assert.False(ControlAt<TextBox>(w, "field:agent-preset.id").IsEnabled);
            name.Text = "updated copy";
            ClickAt(w, "action:agent-preset.save");
            Assert.Equal("version-3", expected);
            Assert.Equal("personal review", (string?)versions.Single(v => (string?)v?["definition"]?["id"] == "user:custom")!["definition"]!["name"]);
            ClickOn("action:agent-preset.copy", (string)source["id"]!);
            name.Text = "discard me";
            ClickAt(w, "action:agent-preset.back");
            w.FindControl<Button>("DiscardSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(name.IsEffectivelyVisible);
            Assert.Equal(4, count);

            void ClickOn(string action, string target)
            {
                var button = w.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && AutomationProperties.GetAutomationId(b) == action && Equals(b.Tag, target));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
            }
        });
}
