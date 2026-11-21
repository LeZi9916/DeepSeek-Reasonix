using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
    /// <summary>Changes typography through the appearance page and observes the mounted shell.</summary>
    /// <returns>The settings interaction.</returns>
    [Fact]
    public Task TypographyUpdatesTheMountedWindow() => WithSettings(
        "settings-appearance",
        (w, reads) =>
        {
            var look = reads.Read<JsonObject>("/appearance");
            look["zoom"] = 1.1;
            var writes = 0;
            reads.Exchange = async req =>
            {
                if (req.RequestUri!.AbsolutePath != "/appearance")
                {
                    return null;
                }

                if (req.Method == HttpMethod.Post)
                {
                    var body = JsonNode.Parse(await req.Content!.ReadAsStringAsync())!;
                    Assert.Equal(1.1, (double?)body["zoom"]);
                    Assert.Equal("zh", (string?)body["language"]);
                    foreach (var key in new[] { "fontUi", "fontMono", "textSize", "readSize" })
                    {
                        look[key] = body[key]?.DeepClone();
                    }

                    writes++;
                }

                return JsonReply(look);
            };
            var heading = w.FindControl<TextBlock>("SettingsHeading")!;
            var size = heading.FontSize;
            var scale = ControlAt<ComboBox>(w, "action:appearance.text-size");
            scale.SelectedIndex = 2;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1.15, (double?)look["textSize"]);
            Assert.Equal(size * 1.15, heading.FontSize, 4);
            var reading = ControlAt<ComboBox>(w, "action:appearance.reading-size");
            reading.SelectedIndex = 2;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(16.5, (double?)look["readSize"]);
            ControlAt<TextBox>(w, "field:appearance.mono").Text = "monospace";
            ClickAt(w, "action:appearance.apply-font");
            Assert.Equal("monospace", (string?)look["fontMono"]);
            Assert.Equal(3, writes);
        });
}
