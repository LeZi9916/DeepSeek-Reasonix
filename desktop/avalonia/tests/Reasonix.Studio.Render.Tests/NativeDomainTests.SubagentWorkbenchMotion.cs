using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Events;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.State;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Opens the matching recorded runs and verifies their retained workbench tabs.</summary>
    /// <param name="dark">Whether to use the dark palette.</param>
    /// <returns>The workbench interaction and capture.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SubagentWorkbenchOpensPagedRecordsInStableTabs(bool dark) =>
        WithSettingsAsync(
            "conversation",
            async (w, reads) =>
            {
                w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                await WaitForMotionFrame(() => w.Classes.Contains("motion")).ConfigureAwait(true);
                var run = new SubagentRun { Ref = "sa_review", Name = "review", Model = "deepseek/deepseek-v4-pro", State = "completed", ParentCall = "review-layout" };
                var cursors = new List<string>();
                var fleet = false;
                reads.Exchange = request =>
                {
                    var path = request.RequestUri!.AbsolutePath;
                    var cursor = request.RequestUri.Query;
                    if (path == "/subagents")
                    {
                        SubagentRun[] runs = fleet
                            ? [run, new SubagentRun { Ref = "sa_second", Name = "second-review", ParentCall = "review-layout" }]
                            : [new SubagentRun { Ref = "sa_other", Name = "other", ParentCall = "different-call" }, run];
                        return Reply(runs);
                    }

                    if (path == "/subagents/sa_review/transcript")
                    {
                        Assert.Equal(HttpMethod.Get, request.Method);
                        cursors.Add(cursor);
                        return Reply(cursor switch
                        {
                            "?after=0" => new SubagentTranscriptPage
                            {
                                Run = run,
                                Next = 1,
                                Total = 2,
                                Messages = [new HistoryMessage { MsgIndex = 1, Role = "user", Content = "核对界面布局。" }],
                            },
                            "?after=1" => new SubagentTranscriptPage
                            {
                                Run = run,
                                Next = 2,
                                Total = 2,
                                Messages = [new HistoryMessage { MsgIndex = 2, Role = "assistant", Content = "已核对标题和状态区的位置。" }],
                            },
                            _ => throw new InvalidOperationException(cursor),
                        });
                    }

                    return Task.FromResult<HttpResponseMessage?>(null);
                };
                var status = reads.Read<SessionStatus>("/status") with
                {
                    Jobs = [],
                    EndedJobs = [],
                };
                w.AdoptComposer(status);
                var reducer = new SessionReducer(new IdSequence());
                var state = Fold(new SessionState(), """{"kind":"turn_started","msgIndex":3,"authoredTurn":3,"text":"请核对界面布局"}""");
                state = Fold(state, """{"kind":"tool_dispatch","tool":{"id":"review-layout","name":"task","args":"{\"description\":\"核对界面布局\",\"subagent_type\":\"review\"}","profile":{"name":"review"}}}""");
                state = Fold(state, """{"kind":"tool_result","tool":{"id":"review-layout","name":"task","args":"{\"description\":\"核对界面布局\",\"subagent_type\":\"review\"}","profile":{"name":"review"},"output":"已核对标题和状态区的位置。"}}""");
                w.ShowState(state);
                w.UpdateLayout();
                var deck = w.FindControl<UserControl>("Subagents")!.FindControl<StudioHoverCard>("Details")!;
                var side = w.FindControl<Border>("WorkbenchPanel")!;
                var host = w.FindControl<ContentControl>("SubagentRunHost")!;
                var tabs = w.FindControl<StackPanel>("FileTabs")!;
                var root = w.FindControl<Grid>("LayoutRoot")!;
                var scheme = dark ? "dark" : "light";
                OpenRun();
                await WaitForMotionFrame(() => host.IsVisible && host.Content is not null).ConfigureAwait(true);
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STUDIO_MOTION_OUTPUT")))
                {
                    await WaitForMotionFrame(() => side.IsAnimating(Layoutable.WidthProperty) && side.Width is > 185 and < 397).ConfigureAwait(true);
                    w.UpdateLayout();
                    CaptureMotionRegion(w, root, 914, -47, 525, 200, $"subagent-workbench-open-{scheme}-38");
                }

                var view = Assert.IsAssignableFrom<UserControl>(host.Content);
                await WaitForMotionFrame(() => view.GetVisualDescendants().OfType<SelectableTextBlock>()
                    .Any(text => (text.Text ?? text.Inlines?.Text) == "已核对标题和状态区的位置。")).ConfigureAwait(true);
                var tab = Assert.Single(tabs.Children);
                await WaitForMotionFrame(() => !side.IsAnimating(Layoutable.WidthProperty) && !tab.IsAnimating(Layoutable.MaxWidthProperty)).ConfigureAwait(true);
                w.UpdateLayout();
                Assert.Equal("核对界面布局。", view.FindControl<SelectableTextBlock>("TaskText")!.Text);
                Assert.Equal("已完成", view.FindControl<TextBlock>("RunState")!.Text);
                Assert.Equal(923, host.TranslatePoint(default, w)!.Value.X, 1);
                Assert.Equal(99, host.TranslatePoint(default, w)!.Value.Y, 1);
                Assert.Equal(508, host.Bounds.Width, 1);
                Assert.Equal(new[] { "?after=0", "?after=1" }, cursors);
                CaptureMotionRegion(w, root, 915, -47, 524, 260, $"subagent-workbench-{scheme}-settled");
                CaptureMotionRegion(w, root, 914, -47, 525, 200, $"subagent-workbench-open-{scheme}-260");

                var back = view.GetVisualDescendants().OfType<Button>()
                    .Single(button => AutomationProperties.GetAutomationId(button) == "action:agent.show-call");
                Click(back);
                var origin = w.GetVisualDescendants().OfType<UserControl>()
                    .Single(control => control.GetType().Name == "ToolCallHeader");
                var originTitle = origin.FindControl<TextBlock>("ToolName")!;
                await WaitForMotionFrame(() => originTitle.Effect is DropShadowEffect).ConfigureAwait(true);
                Assert.Same(view, host.Content);
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STUDIO_MOTION_OUTPUT")))
                {
                    await WaitForMotionFrame(() => originTitle.Effect is DropShadowEffect { Opacity: > 0.115 and < 0.21 }).ConfigureAwait(true);
                    CaptureMotionRegion(w, origin, -10, 10, 581, 48, $"subagent-return-{scheme}-160");
                    await WaitForMotionFrame(() => originTitle.Effect is DropShadowEffect { Opacity: > 0.012 and < 0.063 }).ConfigureAwait(true);
                    CaptureMotionRegion(w, origin, -10, 10, 581, 48, $"subagent-return-{scheme}-380");
                }

                fleet = true;
                OpenRun();
                await WaitForMotionFrame(() => tabs.Children.Count == 2).ConfigureAwait(true);
                Assert.Same(view, host.Content);
                Assert.Equal(2, cursors.Count);
                w.UpdateLayout();
                Click(AgentCloseButton(tabs.Children[1]));
                await WaitForMotionFrame(() => tabs.Children.Count == 1).ConfigureAwait(true);
                Assert.Same(view, host.Content);
                w.UpdateLayout();
                Click(AgentCloseButton(tabs.Children[0]));
                await WaitForMotionFrame(() => !host.IsVisible && tabs.Children.Count == 0).ConfigureAwait(true);

                SessionState Fold(SessionState previous, string json)
                {
                    Assert.True(WireFrame.TryParse(json, out var frame));
                    return reducer.Reduce(previous, frame);
                }

                void OpenRun()
                {
                    deck.Open();
                    w.UpdateLayout();
                    Click(deck.FindControl<Border>("Frame")!.GetVisualDescendants().OfType<Button>()
                        .Single(button => AutomationProperties.GetAutomationId(button) == "action:agent.open"));
                }

                static Button AgentCloseButton(Control element) => element.GetVisualDescendants().OfType<Button>()
                    .Single(button => AutomationProperties.GetAutomationId(button) == "action:workbench.close-agent");

                static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                static Task<HttpResponseMessage?> Reply<T>(T payload) =>
                    Task.FromResult<HttpResponseMessage?>(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) });
            },
            allowMotion: true);

    /// <summary>Ignores an outstanding live reply after a different run is selected.</summary>
    /// <returns>The tab's visibility-scoped read check.</returns>
    [Fact]
    public Task SubagentWorkbenchCancelsLiveReadsOnTabSwitch() =>
        WithSettingsAsync(
            "conversation",
            async (w, reads) =>
            {
                var first = new SubagentRun { Ref = "live-review", Name = "review", State = "running", ParentCall = "call-fleet" };
                var next = new SubagentRun { Ref = "other-review", Name = "verify", State = "completed", ParentCall = "call-fleet" };
                var delayed = new TaskCompletionSource<HttpResponseMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
                var pending = false;
                reads.Exchange = request =>
                {
                    var path = request.RequestUri!.AbsolutePath;
                    if (path == "/subagents")
                    {
                        return Reply(new[] { first, next });
                    }

                    if (path == "/subagents/live-review/transcript")
                    {
                        if (request.RequestUri.Query == "?after=0")
                        {
                            return Reply(new SubagentTranscriptPage
                            {
                                Run = first,
                                Next = 1,
                                Total = 1,
                                Live = true,
                                Messages = [new HistoryMessage { MsgIndex = 1, Role = "user", Content = "核对当前记录。" }],
                            });
                        }

                        Assert.Equal("?after=1", request.RequestUri.Query);
                        pending = true;
                        return delayed.Task;
                    }

                    if (path == "/subagents/other-review/transcript")
                    {
                        return Reply(new SubagentTranscriptPage
                        {
                            Run = next,
                            Next = 1,
                            Total = 1,
                            Messages = [new HistoryMessage { MsgIndex = 1, Role = "assistant", Content = "第二条记录。" }],
                        });
                    }

                    return Task.FromResult<HttpResponseMessage?>(null);
                };
                w.AdoptComposer(reads.Read<SessionStatus>("/status"));
                var reducer = new SessionReducer(new IdSequence());
                Assert.True(WireFrame.TryParse("""{"kind":"tool_dispatch","tool":{"id":"call-fleet","name":"task","profile":{"name":"review","count":2}}}""", out var dispatch));
                w.ShowState(reducer.Reduce(new SessionState(), dispatch));
                w.UpdateLayout();
                var deck = w.FindControl<UserControl>("Subagents")!.FindControl<StudioHoverCard>("Details")!;
                deck.Open();
                w.UpdateLayout();
                var row = deck.FindControl<Border>("Frame")!.GetVisualDescendants().OfType<Button>()
                    .Single(button => AutomationProperties.GetAutomationId(button) == "action:agent.open");
                row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var host = w.FindControl<ContentControl>("SubagentRunHost")!;
                var tabs = w.FindControl<StackPanel>("FileTabs")!;
                await WaitForMotionFrame(() => pending && tabs.Children.Count == 2).ConfigureAwait(true);
                var liveView = Assert.IsAssignableFrom<UserControl>(host.Content);
                w.UpdateLayout();
                tabs.Children[1].GetVisualDescendants().OfType<Button>()
                    .Single(button => AutomationProperties.GetAutomationId(button) == "action:workbench.agent-tab")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitForMotionFrame(() => !ReferenceEquals(liveView, host.Content)).ConfigureAwait(true);
                var selected = Assert.IsAssignableFrom<UserControl>(host.Content);
                delayed.SetResult(ReplyResponse(new SubagentTranscriptPage
                {
                    Run = first,
                    Next = 2,
                    Total = 2,
                    Messages = [new HistoryMessage { MsgIndex = 2, Role = "assistant", Content = "已离开标签后的迟到输出。" }],
                }));
                await WaitForMotionFrame(() => HasLine(selected, "第二条记录。")).ConfigureAwait(true);
                Assert.False(HasLine(liveView, "已离开标签后的迟到输出。"));
                Assert.Same(selected, host.Content);

                static bool HasLine(Control view, string text) => view.GetVisualDescendants().OfType<SelectableTextBlock>()
                    .Any(item => (item.Text ?? item.Inlines?.Text) == text);

                static Task<HttpResponseMessage?> Reply<T>(T payload) => Task.FromResult<HttpResponseMessage?>(ReplyResponse(payload));

                static HttpResponseMessage ReplyResponse<T>(T payload) =>
                    new(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(payload),
                    };
            });
}
