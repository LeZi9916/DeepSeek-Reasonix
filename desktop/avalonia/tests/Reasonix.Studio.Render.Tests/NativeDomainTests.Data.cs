using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Changes the usage period and currency and checks that encrypted export needs two matching passphrases.</summary>
    /// <param name="width">Viewport width.</param>
    /// <returns>The interaction.</returns>
    [Theory]
    [InlineData(1440)]
    [InlineData(760)]
    public Task DataPeriodCurrencyAndExportValidation(int width) => WithSettings(
        "settings-storage",
        (window, reads) =>
        {
            window.Width = width;
            Dispatcher.UIThread.RunJobs();
            var requests = new List<string>();
            reads.Exchange = async request =>
            {
                if (request.RequestUri!.AbsolutePath == "/usage")
                {
                    requests.Add(request.RequestUri.Query);
                    return JsonReply(reads.Read<JsonObject>("/usage"));
                }

                if (request.RequestUri.AbsolutePath == "/display-currency" && request.Method == HttpMethod.Post)
                {
                    var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
                    requests.Add((string)body["mode"]!);
                    return JsonReply(new JsonObject { ["mode"] = "USD" });
                }

                return null;
            };
            var controls = window.FindControl<StackPanel>("SettingsBody")!;
            var seven = controls.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "7 天"));
            seven.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var usd = controls.GetVisualDescendants().OfType<RadioButton>().Single(b => Equals(b.Tag, "USD"));
            usd.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "?days=7", "USD", "?days=7" }, requests);
            Assert.True(usd.IsChecked);
            var phrases = controls.GetVisualDescendants().OfType<TextBox>().Where(b => b.PasswordChar != default).ToArray();
            Assert.Equal(2, phrases.Length);
            var export = controls.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "导出到文件…"));
            Assert.False(export.IsEnabled);
            phrases[0].Text = "fixture passphrase";
            phrases[1].Text = "wrong";
            Dispatcher.UIThread.RunJobs();
            Assert.False(export.IsEnabled);
            phrases[1].Text = phrases[0].Text;
            Dispatcher.UIThread.RunJobs();
            Assert.True(export.IsEnabled);
        });
}
