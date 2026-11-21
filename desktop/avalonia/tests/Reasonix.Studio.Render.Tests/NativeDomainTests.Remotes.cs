using Avalonia.Controls;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks that saving a connection writes its form and probing uses the saved machine.</summary>
    /// <returns>The native interaction.</returns>
    [Fact]
    public Task RemoteBookSavesProbesAndRemoves() => WithSettings(
        "settings-network",
        (w, reads) =>
        {
            var book = new JsonArray();
            var probes = 0;
            reads.Exchange = async req =>
            {
                var path = req.RequestUri!.AbsolutePath;
                if (path == "/remotes")
                {
                    if (req.Method == HttpMethod.Post)
                    {
                        var entry = JsonNode.Parse(await req.Content!.ReadAsStringAsync())!;
                        Assert.Equal("buildbox", (string?)entry["name"]);
                        Assert.Equal("lab.example.invalid", (string?)entry["host"]);
                        book.Add(entry);
                    }

                    return JsonReply(book);
                }

                if (path == "/remotes/buildbox/probe")
                {
                    probes++;
                    return JsonReply(new JsonObject { ["ready"] = true, ["os"] = "linux" });
                }

                if (path == "/remotes/remove")
                {
                    Assert.Equal("buildbox", (string?)JsonNode.Parse(await req.Content!.ReadAsStringAsync())?["name"]);
                    book.Clear();
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }

                return null;
            };
            ClickAt(w, "action:remote.add");
            ControlAt<TextBox>(w, "field:remote.name").Text = "buildbox";
            ControlAt<TextBox>(w, "field:remote.host").Text = "lab.example.invalid";
            ClickAt(w, "action:remote.save");
            Assert.Single(book);
            Assert.Equal(0, probes);
            ClickAt(w, "action:remote.probe");
            Assert.Equal(1, probes);
            ClickAt(w, "action:remote.remove");
            Assert.Single(book);
            ClickAt(w, "action:remote.confirm-remove");
            Assert.Empty(book);
        });
}
