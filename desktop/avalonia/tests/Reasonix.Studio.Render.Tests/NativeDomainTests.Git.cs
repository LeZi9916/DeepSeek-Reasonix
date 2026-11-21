using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks index, discard, history and review actions against a nested repository.</summary>
    /// <returns>The workbench interaction.</returns>
    [Fact]
    public Task WorkbenchGitBindsEveryActionToTheReviewedRepo() => WithSettings(
        "workbench",
        (w, reads) =>
        {
            var calls = new List<string>();
            var change = new JsonObject { ["path"] = "dir/a b.txt", ["status"] = "M", ["staged"] = false, ["unstaged"] = true, ["insertions"] = 2, ["deletions"] = 1 };
            var plan = JsonNode.Parse("""{"remote":"origin","branch":"main","upstreamBranch":"main","scheme":"https","host":"example.invalid","url":"https://example.invalid/team/git","head":"abcdef","ahead":2,"behind":0}""")!;
            reads.Exchange = async request =>
            {
                var uri = request.RequestUri!;
                var path = uri.AbsolutePath;
                if (path == "/git/repos")
                {
                    return JsonReply(JsonNode.Parse("""{"repos":[{"dir":"nested","name":"nested","branch":"main"}],"truncated":false}""")!);
                }

                if (path is "/changes" or "/workspace/git/log" or "/git/commit" or "/git/commit/diff" or "/changes/diff" || (path == "/git/push" && request.Method == HttpMethod.Get))
                {
                    Assert.Contains("repo=nested", uri.Query);
                    calls.Add("read " + path);
                    return path switch
                    {
                        "/changes" => JsonReply(new JsonObject { ["repo"] = true, ["changes"] = new JsonArray(change.DeepClone()) }),
                        "/workspace/git/log" => JsonReply(JsonNode.Parse("""{"repo":true,"commits":[{"id":"abc123","subject":"first","parents":[],"refs":["main"],"author":"tester","at":1}]}""")!),
                        "/git/commit" => JsonReply(JsonNode.Parse("""{"repo":true,"files":[{"path":"dir/a b.txt","status":"M"}]}""")!),
                        "/git/push" => JsonReply(plan),
                        _ => JsonReply(new JsonObject { ["diff"] = "@@ -1 +1 @@\n-old\n+new\n" }),
                    };
                }

                if (request.Method != HttpMethod.Post)
                {
                    return null;
                }

                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
                calls.Add("write " + path);
                if (path == "/commit/propose")
                {
                    Assert.Contains("repo=nested", uri.Query);
                    return JsonReply(JsonNode.Parse("""{"fingerprint":"reviewed-index","message":"draft","contentSecrets":true,"files":[{"status":"M","path":"dir/a b.txt"}]}""")!);
                }

                Assert.Equal("nested", (string?)body["repo"]);
                switch (path)
                {
                    case "/changes/stage":
                        Assert.Equal("dir/a b.txt", (string?)body["paths"]?[0]);
                        change["staged"] = (bool?)body["unstage"] != true;
                        change["unstaged"] = (bool?)body["unstage"] == true;
                        break;
                    case "/changes/discard":
                        Assert.Equal("dir/a b.txt", (string?)body["path"]);
                        break;
                    case "/commit":
                        Assert.Equal("reviewed-index", (string?)body["fingerprint"]);
                        Assert.Equal("reviewed message", (string?)body["message"]);
                        Assert.True((bool?)body["acknowledgeSecrets"]);
                        return JsonReply(new JsonObject { ["hash"] = "def456", ["subject"] = "reviewed message" });
                    case "/git/push":
                        Assert.True(JsonNode.DeepEquals(plan, body["plan"]));
                        return JsonReply(new JsonObject { ["plan"] = plan.DeepClone() });
                    default:
                        throw new InvalidOperationException(path);
                }

                return new HttpResponseMessage(HttpStatusCode.NoContent);
            };
            ClickAt(w, "action:workbench.repos-reload");
            ClickAt(w, "action:workbench.stage");
            ClickAt(w, "action:workbench.stage");
            ClickAt(w, "action:workbench.discard");
            ClickAt(w, "action:workbench.discard-cancel");
            Assert.DoesNotContain("write /changes/discard", calls);
            ClickAt(w, "action:workbench.discard");
            ClickAt(w, "action:workbench.discard-confirm");
            ClickAt(w, "action:workbench.commit");
            ClickAt(w, "action:workbench.commit-file");
            Assert.Contains("read /git/commit/diff", calls);
            ClickAt(w, "action:commit.draft");
            var confirm = ControlAt<Button>(w, "action:commit.confirm");
            Assert.False(confirm.IsEnabled);
            ControlAt<TextBox>(w, "action:commit.edit").Text = "reviewed message";
            ControlAt<CheckBox>(w, "action:commit.acknowledge").IsChecked = true;
            ClickAt(w, "action:commit.confirm");
            ClickAt(w, "action:push.preview");
            ClickAt(w, "action:push.close");
            Assert.DoesNotContain("write /git/push", calls);
            ClickAt(w, "action:push.preview");
            ClickAt(w, "action:push.confirm");
            Assert.Equal(1, calls.Count(c => c == "write /commit"));
            Assert.Equal(1, calls.Count(c => c == "write /git/push"));
            Assert.Equal(1, calls.Count(c => c == "write /changes/discard"));
            plan["ahead"] = 0;
            ClickAt(w, "action:push.preview");
            Assert.False(ControlAt<Button>(w, "action:push.confirm").IsEnabled);
        });

    /// <summary>Identifies the workbench shell and Git controls in the full window report.</summary>
    /// <param name="id">Observed identity.</param>
    /// <returns>Whether geometry belongs to the workbench.</returns>
    private static bool IsWorkbenchGeometry(string id) => new[] { "class:workbench", "action:workbench.", "action:commit.", "action:push.", "action:browser.full", "role:tablist" }.Any(prefix => id.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>Captures the expanded changes board and an unconfirmed index review.</summary>
    /// <param name="window">The open workbench.</param>
    /// <param name="output">Native render folder.</param>
    /// <param name="file">Named viewport and color scheme.</param>
    private static void GitScreenProofs(Reasonix.Studio.Shell.MainWindow window, string output, string file)
    {
        ClickAt(window, "action:workbench.changes-page");
        Save("changes");
        var board = window.FindControl<ContentControl>("RenderedFile")!;
        var prepare = board.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "action:commit.draft");
        prepare.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(ControlAt<TextBox>(window, "action:commit.edit").IsEffectivelyVisible);
        Save("commit");

        void Save(string state)
        {
            using var image = new Avalonia.Media.Imaging.RenderTargetBitmap(Avalonia.PixelSize.FromSize(window.ClientSize, 1), new Avalonia.Vector(96, 96));
            image.Render(window);
            var stem = System.IO.Path.GetFileNameWithoutExtension(file).Replace("workbench", "workbench-" + state, StringComparison.Ordinal);
            image.Save(System.IO.Path.Combine(output, stem + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }
}
