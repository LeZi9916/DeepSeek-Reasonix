using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Chooses an advertised relay protocol and reaches the optional workspace step without saving twice.</summary>
    /// <returns>The native setup flow.</returns>
    [Fact]
    public Task SetupChoosesProtocolAndCanSkipProject() => WithSettingsAsync(
        "onboarding",
        async (w, reads) =>
        {
            var creations = 0;
            reads.Exchange = async req =>
            {
                switch (req.RequestUri!.AbsolutePath)
                {
                    case "/providers/protocols":
                        return JsonReply(JsonNode.Parse("""[{"kind":"openai"},{"kind":"responses"}]""")!);
                    case "/providers/probe":
                        return JsonReply(JsonNode.Parse("""{"kind":"openai","kinds":["openai","responses"],"ambiguous":true,"models":["chat"],"default":"chat","vision":[]}""")!);
                    case "/providers" when req.Method == HttpMethod.Post:
                        var provider = JsonNode.Parse(await req.Content!.ReadAsStringAsync())!;
                        Assert.Equal("responses", (string?)provider["kind"]);
                        creations++;
                        return new HttpResponseMessage(HttpStatusCode.NoContent);
                    case "/model" or "/default-model":
                        return new HttpResponseMessage(HttpStatusCode.NoContent);
                    case "/provider-setup":
                        return JsonReply(new JsonObject { ["required"] = false });
                    default:
                        return null;
                }
            };
            void Click(string button)
            {
                w.FindControl<Button>(button)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
            }

            Click("BeginSetup");
            w.FindControl<Panel>("SetupServices")!.Children.OfType<RadioButton>().Last().IsChecked = true;
            w.FindControl<TextBox>("ServiceAddress")!.Text = "https://relay.example.invalid";
            w.FindControl<TextBox>("ServiceName")!.Text = "relay";
            Dispatcher.UIThread.RunJobs();
            w.FindControl<TextBox>("ServiceKey")!.Text = "fixture";
            Dispatcher.UIThread.RunJobs();
            Click("ConnectProvider");
            await WaitForMotionFrame(() => w.FindControl<ComboBox>("ServiceProtocol")!.IsEffectivelyVisible).ConfigureAwait(true);
            var protocols = ControlAt<ComboBox>(w, "action:onboarding.protocol");
            protocols.SelectedIndex = 1;
            Click("ConnectProvider");
            Assert.Equal(1, creations);
            ClickAt(w, "action:onboarding.skip-project");
            Assert.True(w.FindControl<Grid>("LayoutRoot")!.IsVisible);
            Assert.Equal(1, creations);
        });
}
