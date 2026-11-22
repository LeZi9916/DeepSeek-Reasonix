using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Composer;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks focused editing and the complete toolbar in each supported viewport and palette.</summary>
    /// <param name="width">Window width.</param>
    /// <param name="dark">Requested palette.</param>
    /// <returns>The mounted interaction.</returns>
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(760, false)]
    [InlineData(760, true)]
    public Task ComposerKeepsReferenceFocusAndTools(int width, bool dark) => WithSettings("conversation", (w, reads) =>
    {
        w.Width = width;
        w.Height = width == 760 ? 480 : 900;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        w.AdoptComposer(reads.Read<SessionStatus>("/status"));
        var composer = w.FindControl<TextBox>("Composer")!;
        composer.Text = "对齐聊天区的字体、颜色与消息间距。";
        composer.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(default(Thickness), composer.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_BorderElement").BorderThickness);
        Assert.Contains("Reasonix Workbench UI", composer.FontFamily.Name);
        Assert.Equal(FontWeight.Medium, composer.FontWeight);
        Assert.Equal(22.4, composer.LineHeight);
        foreach (var name in new[] { "Attach", "SideQuestion", "RefinePrompt", "PlanMode", "ComposeBranch", "ApprovalMode", "ModelButton", "EffortButton", "Send", "CacheToggle", "ContextToggle", "HideAmounts" })
        {
            var button = Assert.IsAssignableFrom<Button>(w.FindControl<Control>(name));
            Assert.True(button.IsEffectivelyVisible, name);
            var point = button.TranslatePoint(default, w)!.Value;
            Assert.InRange(point.X, 0, width - button.Bounds.Width);
            Assert.True(button.Bounds.Height <= 32, name);
        }

        Assert.True(w.FindControl<Button>("ChangedFiles")!.IsVisible);
        Assert.Equal(32, w.FindControl<Button>("Send")!.Bounds.Height);
        composer.Text = string.Empty;
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.FindControl<Button>("Send")!.IsEnabled);
    });

    /// <summary>Mode changes use kernel readback while a refusal preserves the draft and previous mode.</summary>
    /// <returns>The mode interaction.</returns>
    [Fact]
    public Task ComposerModesReadBackWithoutReplacingDraft() => WithSettings("conversation", (w, reads) =>
    {
        var status = reads.Read<JsonObject>("/status");
        var refuse = false;
        reads.Exchange = async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/status")
            {
                return JsonReply(status);
            }

            if (request.RequestUri.AbsolutePath is not ("/plan" or "/tool-approval-mode"))
            {
                return null;
            }

            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
            if (refuse)
            {
                return new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("refused") };
            }

            if (request.RequestUri.AbsolutePath == "/plan")
            {
                status["plan"] = (bool)body["on"]!;
            }
            else
            {
                status["toolApprovalMode"] = (string)body["mode"]!;
            }

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        };
        w.AdoptComposer(reads.Read<SessionStatus>("/status"));
        var input = w.FindControl<TextBox>("Composer")!;
        input.Text = "keep this draft";
        Click("PlanMode");
        var modePopup = w.FindControl<ComposerPopup>("ModePopup")!;
        Assert.True(modePopup.IsOpen);
        var modePanel = w.FindControl<ModePanel>("ModePanel")!;
        modePanel.FindControl<Button>("PlanOption")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Plan", w.FindControl<TextBlock>("PlanModeLabel")!.Text);
        Assert.Equal("keep this draft", input.Text);
        Assert.False(modePopup.IsOpen);
        refuse = true;
        Click("ApprovalMode");
        var policyPopup = w.FindControl<ComposerPopup>("PolicyPopup")!;
        Assert.True(policyPopup.IsOpen);
        var policyPanel = w.FindControl<PolicyPanel>("PolicyPanel")!;
        policyPanel.FindControl<Button>("AutoOption")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("询问", w.FindControl<TextBlock>("ApprovalModeLabel")!.Text);
        Assert.Equal("keep this draft", input.Text);
        Assert.True(policyPopup.IsOpen);
        Assert.True(policyPanel.FindControl<TextBlock>("ErrorText")!.IsVisible);
        void Click(string name) => w.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    });

    /// <summary>The branch palette searches local branches and applies a switch.</summary>
    /// <returns>The branch interaction.</returns>
    [Fact]
    public Task ComposerBranchPaletteSearchesAndSwitches() => WithSettings("conversation", (w, reads) =>
    {
        var calls = new List<string>();
        reads.Exchange = async request =>
        {
            calls.Add(request.RequestUri!.AbsolutePath);
            if (request.RequestUri.AbsolutePath != "/workspace/branch/switch")
            {
                return null;
            }

            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
            Assert.Equal("studio", (string?)body["name"]);
            return JsonReply(new JsonObject { ["branch"] = "studio" });
        };
        w.AdoptComposer(reads.Read<SessionStatus>("/status"));
        Click("ComposeBranch");
        Dispatcher.UIThread.RunJobs();
        var popup = w.FindControl<ComposerPopup>("BranchPopup")!;
        var panel = w.FindControl<BranchPanel>("BranchPanel")!;
        Assert.True(popup.IsOpen);
        panel.FindControl<TextBox>("Search")!.Text = "stu";
        Dispatcher.UIThread.RunJobs();
        var row = panel.FindControl<StackPanel>("Rows")!.Children.OfType<Button>().Single();
        Assert.Equal("studio", (string?)row.Tag);
        row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var switched = SpinWait.SpinUntil(
            () =>
            {
                Dispatcher.UIThread.RunJobs();
                return w.FindControl<TextBlock>("ComposeBranchLabel")!.Text == "studio";
            },
            System.TimeSpan.FromSeconds(2));
        Assert.True(switched, string.Join(",", calls) + " / " + panel.FindControl<TextBlock>("ErrorText")?.Text);
        Assert.False(popup.IsOpen);
        void Click(string name) => w.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    });

    /// <summary>The branch palette offers a worktree source mode when the host can isolate.</summary>
    /// <returns>The worktree source choice.</returns>
    [Fact]
    public Task ComposerBranchPaletteOffersIsolatedSources() => WithSettings("conversation", (w, _) =>
    {
        var panel = w.FindControl<BranchPanel>("BranchPanel")!;
        var sources = new WorktreeSources
        {
            Available = true,
            SourceRoot = "/repo",
            Sources = [new WorktreeSource { Ref = "refs/heads/topic", Name = "topic" }],
        };
        string? isolated = null;
        panel.IsolateRequested += reference => isolated = reference;
        panel.Show([new BranchChoice("main", true, string.Empty)], "main", "/repo", InterfaceLanguage.Zh);
        Dispatcher.UIThread.RunJobs();
        Assert.False(panel.FindControl<Border>("WorktreeModes")!.IsVisible);
        panel.Show([new BranchChoice("main", true, string.Empty)], "main", "/repo", InterfaceLanguage.Zh, sources);
        Dispatcher.UIThread.RunJobs();
        Assert.True(panel.FindControl<Border>("WorktreeModes")!.IsVisible);
        panel.FindControl<Button>("IsolatedMode")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var row = panel.FindControl<StackPanel>("Rows")!.Children.OfType<Button>().Single();
        row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("refs/heads/topic", isolated);
    });

    /// <summary>A proposed rewrite stays separate until adopted, and a refusal leaves the original editable.</summary>
    /// <returns>The rewrite interaction.</returns>
    [Fact]
    public Task ComposerReviewsRewriteBeforeAdoption() => WithSettings("conversation", (w, reads) =>
    {
        var refuse = false;
        reads.Exchange = async request =>
        {
            if (request.RequestUri!.AbsolutePath != "/prompt/refine")
            {
                return null;
            }

            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
            Assert.False(string.IsNullOrWhiteSpace((string?)body["draft"]));
            return refuse ? new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("refused") } : JsonReply(new JsonObject { ["text"] = "clearer proposal" });
        };
        var input = w.FindControl<TextBox>("Composer")!;
        input.Text = "original draft";
        Click("RefinePrompt");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("original draft", input.Text);
        Assert.Equal("clearer proposal", w.FindControl<SelectableTextBlock>("RefineText")!.Text);
        input.Text = "edited while reviewing";
        Click("DiscardRefine");
        Assert.Equal("edited while reviewing", input.Text);
        Assert.False(w.FindControl<Border>("RefineCard")!.IsVisible);
        Click("RefinePrompt");
        Dispatcher.UIThread.RunJobs();
        Click("AdoptRefine");
        Assert.Equal("clearer proposal", input.Text);
        refuse = true;
        Click("RefinePrompt");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("clearer proposal", input.Text);
        Assert.False(w.FindControl<Button>("AdoptRefine")!.IsVisible);
        Assert.True(w.FindControl<Button>("RetryRefine")!.IsVisible);
        void Click(string name) => w.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    });

    /// <summary>Side questions use their own stream and never send or clear the main draft.</summary>
    /// <returns>The isolated side conversation.</returns>
    [Fact]
    public Task ComposerSideQuestionKeepsMainDraft() => WithSettings("conversation", (w, reads) =>
    {
        var closed = false;
        reads.Exchange = async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/side")
            {
                return JsonReply(new JsonObject { ["id"] = "side-test" });
            }

            if (request.RequestUri.AbsolutePath == "/side/side-test/ask")
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
                Assert.Equal("separate question", (string?)body["question"]);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("event: done\ndata: {\"answer\":\"isolated answer\"}\n\n", System.Text.Encoding.UTF8, "text/event-stream") };
            }

            if (request.RequestUri.AbsolutePath == "/side/side-test")
            {
                Assert.Equal(HttpMethod.Delete, request.Method);
                closed = true;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return null;
        };
        var input = w.FindControl<TextBox>("Composer")!;
        input.Text = "main draft";
        Click("SideQuestion");
        Dispatcher.UIThread.RunJobs();
        w.FindControl<TextBox>("SideInput")!.Text = "separate question";
        Click("AskSide");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("isolated answer", w.FindControl<SelectableTextBlock>("SideAnswer")!.Text);
        Assert.Equal("main draft", input.Text);
        Click("CloseSide");
        Dispatcher.UIThread.RunJobs();
        Assert.True(closed);
        Assert.Equal("main draft", input.Text);
        Assert.False(w.FindControl<Border>("SideCard")!.IsVisible);
        void Click(string name) => w.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    });

    /// <summary>A side conversation created after its panel closes is retired on its original runtime.</summary>
    /// <returns>The delayed creation and close.</returns>
    [Fact]
    public Task ComposerRetiresSideOpenedAfterClose() => WithSettings("conversation", (w, reads) =>
    {
        var pending = new TaskCompletionSource<HttpResponseMessage?>();
        var closed = false;
        reads.Exchange = request =>
        {
            if (request.RequestUri!.AbsolutePath == "/side")
            {
                return pending.Task;
            }

            if (request.RequestUri.AbsolutePath == "/side/late")
            {
                Assert.Equal(HttpMethod.Delete, request.Method);
                closed = true;
                return Task.FromResult<HttpResponseMessage?>(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            return Task.FromResult<HttpResponseMessage?>(null);
        };
        w.FindControl<Button>("SideQuestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        w.FindControl<Button>("CloseSide")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        pending.SetResult(JsonReply(new JsonObject { ["id"] = "late" }));
        var retired = SpinWait.SpinUntil(
            () =>
            {
                Dispatcher.UIThread.RunJobs();
                return closed;
            },
            System.TimeSpan.FromSeconds(2));
        Assert.True(retired);
        Assert.False(w.FindControl<Border>("SideCard")!.IsVisible);
    });
}
