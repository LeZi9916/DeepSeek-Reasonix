using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Events;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.Pages;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.Shell;
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
    /// <summary>Uses the unread timestamp reported by the rail before clearing its unread mark.</summary>
    /// <param name="width">The viewport width.</param><param name="dark">Whether to use the dark theme.</param>
    /// <returns>The navigation and display-setting interaction.</returns>
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(760, false)]
    [InlineData(760, true)]
    public async Task NewAnswerSettingUsesTheUnreadSpanFromTheSessionRow(int width, bool dark)
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(RasterBuilder));
        await session.Dispatch(
            () =>
            {
                using var reads = new ReferenceReads();
                using var http = new HttpClient(reads) { BaseAddress = new Uri("http://127.0.0.1:4000") };
                var viewed = 0;
                const string Path = "/sessions/mock.jsonl";
                reads.Exchange = request =>
                {
                    var route = request.RequestUri!.AbsolutePath;
                    return Task.FromResult<HttpResponseMessage?>(route switch
                    {
                        "/tree/sessions/viewed" => Viewed(),
                        "/runtimes" => JsonReply(JsonNode.Parse("""{"id":"r2","base":"/rt/r2","root":"/work","sessionPath":"/sessions/mock.jsonl"}""")!),
                        "/history/page" => new HttpResponseMessage(HttpStatusCode.NotFound),
                        "/history" => JsonReply(JsonNode.Parse("""
                            [
                              {"role":"user","content":"先确认现状。","createdAt":1700000000100,"msgIndex":1},
                              {"role":"assistant","content":"现有界面仍在运行，可以直接比对。","msgIndex":2},
                              {"role":"user","content":"请核对这两处差异。","createdAt":1700000000150,"msgIndex":3},
                              {"role":"assistant","content":"第一处是顶部间距，第二处是输入框边框。","msgIndex":4},
                              {"role":"user","content":"请把对比结果记录下来。","createdAt":1700000000350,"msgIndex":5},
                              {"role":"assistant","content":"已记录对应状态和局部截图。","msgIndex":6}
                            ]
                            """)!),
                        "/status" => JsonReply(new JsonObject { ["sessionPath"] = Path, ["workspaceRoot"] = "/work", ["label"] = "比对会话" }),
                        "/events" or "/tree/events" => JsonReply(new JsonArray()),
                        _ => null,
                    });
                };
                SessionEngine MakeEngine(RuntimeDescriptor? runtime = null) => new(new KernelClient(http), new SseEventTransport(http.BaseAddress.ToString(), http), descriptor => MakeEngine(descriptor), runtime);
                var height = width == 1440 ? 900 : 480;
                var w = new MainWindow(false, MakeEngine(), InterfaceLanguage.Zh, _ => Task.FromResult(false))
                {
                    Width = width,
                    Height = height,
                    RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
                };
                w.Show();
                try
                {
                    w.AdoptComposer(new SessionStatus { WorkspaceRoot = "/work", SessionPath = "/sessions/other.jsonl" });
                    var rail = w.FindControl<ProjectRail>("Projects")!;
                    rail.Show(
                        new[]
                        {
                            new TreeWorkspace
                            {
                                Root = "/work",
                                Name = "work",
                                Sessions = new[] { new TreeSession { Path = Path, Title = "比对会话", Unread = true, ViewedAt = 1700000000200 } },
                            },
                        },
                        "/sessions/other.jsonl");
                    Dispatcher.UIThread.RunJobs();
                    rail.Navigate(new SessionOpenTarget(string.Empty, "/work", Path));
                    var page = w.FindControl<SessionPage>("Conversation")!;
                    PumpNavigation(() => viewed > 0 && page.State.Items.Count >= 6);
                    var marker = Assert.Single(page.GetVisualDescendants().OfType<Control>(), c => AutomationProperties.GetAutomationId(c) == "class:new-answer-line");
                    var text = page.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "请把对比结果记录下来。");
                    var turn = text.GetVisualAncestors().OfType<Border>().First(c => AutomationProperties.GetAutomationId(c) == "class:call");
                    Assert.Equal(marker.TranslatePoint(default, page)!.Value.Y + marker.Bounds.Height, turn.TranslatePoint(default, page)!.Value.Y, precision: 1);
                    marker.BringIntoView();
                    Dispatcher.UIThread.RunJobs();
                    CaptureFlow(w, $"settings-new-answer-turn-{width}x{height}");

                    Navigate(w, "settings-display");
                    var toggle = ControlAt<ToggleSwitch>(w, "action:appearance.new-answer-marker");
                    Assert.True(toggle.IsChecked);
                    var presentFiles = w.GetVisualDescendants().OfType<Border>().Single(control => Equals(control.Tag, "settings.ai_file_delivery_cards"));
                    Assert.Contains(presentFiles.GetVisualDescendants().OfType<TextBlock>(), label => label.Text == "新开的会话生效");
                    var scroller = w.FindControl<ScrollViewer>("SettingsScroller")!;
                    scroller.Offset = new Vector(0, scroller.Extent.Height);
                    CaptureFlow(w, $"settings-new-answer-setting-on-{width}x{height}", resetScroll: false);
                    toggle.IsChecked = false;
                    Dispatcher.UIThread.RunJobs();
                    CaptureFlow(w, $"settings-new-answer-setting-off-{width}x{height}", resetScroll: false);
                    ClickAt(w, "action:settings.close");
                    Assert.DoesNotContain(page.GetVisualDescendants().OfType<Control>(), c => AutomationProperties.GetAutomationId(c) == "class:new-answer-line");
                    CaptureFlow(w, $"settings-new-answer-off-{width}x{height}");

                    Navigate(w, "settings-display");
                    ControlAt<ToggleSwitch>(w, "action:appearance.new-answer-marker").IsChecked = true;
                    ClickAt(w, "action:settings.close");
                    var state = page.State with
                    {
                        Items = page.State.Items.Take(4).ToArray(),
                    };
                    w.ShowState(state);
                    Dispatcher.UIThread.RunJobs();
                    var replyMarker = Assert.Single(page.GetVisualDescendants().OfType<Control>(), c => AutomationProperties.GetAutomationId(c) == "class:new-answer-line");
                    var prompt = page.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "请核对这两处差异。");
                    var reply = page.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "第一处是顶部间距，第二处是输入框边框。");
                    var markerY = replyMarker.TranslatePoint(default, page)!.Value.Y;
                    Assert.True(prompt.TranslatePoint(default, page)!.Value.Y < markerY);
                    Assert.True(markerY < reply.TranslatePoint(default, page)!.Value.Y);
                    replyMarker.BringIntoView();
                    Dispatcher.UIThread.RunJobs();
                    CaptureFlow(w, $"settings-new-answer-reply-{width}x{height}");
                }
                finally
                {
                    w.Close();
                }

                HttpResponseMessage Viewed()
                {
                    viewed++;
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }
            },
            default);
    }
}
