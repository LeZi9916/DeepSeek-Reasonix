using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Controls;
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

    /// <summary>Checks the reference palette grid, label block and confirmed theme removal.</summary>
    /// <returns>The appearance interaction.</returns>
    [Fact]
    public Task ThemeCardsFillTheReferenceGridAndRemoveOnlyAfterConfirmation() => WithSettings(
        "settings-appearance",
        (w, reads) =>
        {
            var themes = reads.Read<JsonArray>("/themes");
            var removed = 0;
            reads.Exchange = request =>
            {
                if (request.Method == HttpMethod.Delete)
                {
                    var id = request.RequestUri!.AbsolutePath.Split('/').Last();
                    if (themes.FirstOrDefault(theme => (string?)theme?["id"] == id) is { } item)
                    {
                        themes.Remove(item);
                    }

                    removed++;
                    return Task.FromResult<HttpResponseMessage?>(new HttpResponseMessage(HttpStatusCode.NoContent));
                }

                return Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath == "/themes" ? JsonReply(themes) : null);
            };

            Button[] Cards() => w.GetVisualDescendants().OfType<Button>()
                .Where(button => AutomationProperties.GetAutomationId(button) == "action:theme.activate").ToArray();
            Assert.Equal(5, Cards().Length);
            var frames = w.GetVisualDescendants().OfType<Border>()
                .Where(border => border.Child is AspectRatioView).ToArray();
            Assert.All(frames, border => Assert.Equal(1, border.BorderThickness.Left));
            var label = w.GetVisualDescendants().OfType<TextBlock>()
                .First(block => block.Text == "明暗" && block.Classes.Contains("setting-row-name"));
            Assert.Equal(FontWeight.SemiBold, label.FontWeight);
            var selectedRing = w.GetVisualDescendants().OfType<Border>()
                .Single(border => border.BorderThickness.Left == 2 && border.CornerRadius.TopLeft == 7);
            Assert.Equal(2, selectedRing.BorderThickness.Left);
            var first = Cards()[0];
            Assert.Equal(212.4, first.Bounds.Width, 1);
            Assert.InRange(first.Bounds.Height, 173, 175);
            var cell = Assert.IsType<StackPanel>(Cards().Single(button => AutomationProperties.GetName(button) == "Dusk").Parent);
            var remove = Assert.Single(cell.Children.OfType<Button>(), button =>
                AutomationProperties.GetAutomationId(button) == "action:theme.remove");
            Assert.Equal("删除", remove.Content);
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("确认删除", remove.Content);
            Assert.Equal(0, removed);
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpNavigation(() => removed == 1 && Cards().Length == 4);
            Assert.DoesNotContain(Cards(), button => AutomationProperties.GetName(button) == "Dusk");
        });
}
