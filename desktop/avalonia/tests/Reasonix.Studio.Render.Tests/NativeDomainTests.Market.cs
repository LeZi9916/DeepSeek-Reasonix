using Avalonia.Controls;
using System.Net;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Installs an update only after reviewing its returned plan and acknowledging its skills.</summary>
    /// <returns>The interaction.</returns>
    [Fact]
    public Task MarketplaceAppliesTheReviewedUpdate() => WithSettings(
        "settings-ext-market",
        (w, reads) =>
        {
            var package = reads.Read<JsonObject>("/market/packages")["packages"]![0]!;
            package["kind"] = "plugin";
            var slug = (string)package["slug"]!;
            var installs = 0;
            var plans = 0;
            reads.Exchange = async req =>
            {
                var path = req.RequestUri!.AbsolutePath;
                if (path == "/market/packages/" + slug)
                {
                    return JsonReply(new JsonObject { ["pinned"] = true, ["installed"] = new JsonObject { ["version"] = "0.1" }, ["package"] = package.DeepClone() });
                }

                if (path is "/market/plan" or "/market/install")
                {
                    var body = JsonNode.Parse(await req.Content!.ReadAsStringAsync())!;
                    Assert.True((bool?)body["replace"]);
                    Assert.Equal(slug, (string?)body["slug"]);
                    if (path == "/market/install")
                    {
                        Assert.Equal("review-token", (string?)body["planId"]);
                        Assert.Equal("1.0.0", (string?)body["version"]);
                        installs++;
                        return JsonReply(new JsonObject { ["ok"] = true, ["applied"] = true });
                    }

                    plans++;
                    return JsonReply(JsonNode.Parse("""{"ok":true,"version":"1.0.0","planId":"review-token","kinds":{"skill":2},"actions":[{"kind":"skill","name":"one"},{"kind":"skill","name":"two"}]}""")!);
                }

                return null;
            };
            ClickAt(w, "action:market.open");
            ClickAt(w, "action:market.inspect");
            Assert.Equal(0, installs);
            ClickAt(w, "action:market.cancel");
            Assert.Equal(0, installs);
            ClickAt(w, "action:market.inspect");
            Assert.False(ControlAt<Button>(w, "action:market.install").IsEnabled);
            ControlAt<CheckBox>(w, "action:market.confirm-many").IsChecked = true;
            ClickAt(w, "action:market.install");
            Assert.Equal(2, plans);
            Assert.Equal(1, installs);
        });
}
