using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Measures all feedback phases through the installed-extensions reload action.</summary>
    /// <param name="dark">Whether to use the dark palette.</param>
    /// <returns>The extension reload and capture.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RuntimeReloadFollowsTheRuntimeAcknowledgement(bool dark) =>
        WithSettingsAsync(
            "conversation",
            async (w, reads) =>
            {
                w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                await WaitForMotionFrame(() => w.Classes.Contains("motion")).ConfigureAwait(true);
                var first = new TaskCompletionSource<HttpResponseMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
                var refused = new TaskCompletionSource<HttpResponseMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
                var settled = new TaskCompletionSource<HttpResponseMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
                var attempts = 0;
                reads.Exchange = request =>
                {
                    if (request.RequestUri!.AbsolutePath != "/extensions/reload")
                    {
                        return Task.FromResult<HttpResponseMessage?>(null);
                    }

                    Assert.Equal(HttpMethod.Post, request.Method);
                    return ++attempts switch
                    {
                        1 => first.Task,
                        2 => refused.Task,
                        _ => settled.Task,
                    };
                };
                Navigate(w, "settings-ext");
                var content = w.FindControl<ContentControl>("SettingsContent")!;
                await WaitForMotionFrame(() => content.GetVisualDescendants().OfType<Button>()
                    .Any(button => AutomationProperties.GetAutomationId(button) == "action:extensions.reload")).ConfigureAwait(true);
                var view = content.GetVisualDescendants().OfType<UserControl>().Single(control => control.GetType().Name == "InstalledExtensionsView");
                var reload = view.FindControl<Button>("Reload")!;
                var label = view.FindControl<TextBlock>("ReloadLabel")!;
                var note = view.FindControl<TextBlock>("ReloadNote")!;
                var foot = view.FindControl<Border>("ReloadFoot")!;
                var dot = view.FindControl<Ellipse>("ReloadDot")!;
                var scheme = dark ? "dark" : "light";
                var capture = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STUDIO_MOTION_OUTPUT"));

                Assert.True(reload.IsEnabled);
                Assert.Equal("重载运行时", label.Text);
                Assert.False(note.IsVisible);
                Capture("offer");
                Click(reload);
                await WaitForMotionFrame(() => attempts == 1 && !reload.IsEnabled).ConfigureAwait(true);
                Assert.Equal("重载中", label.Text);
                Assert.Contains("重新扫描技能、命令和钩子", note.Text, StringComparison.Ordinal);
                if (capture)
                {
                    await WaitForMotionFrame(() => dot.Opacity is >= 0.4 and < 0.49).ConfigureAwait(true);
                    Capture("run");
                }

                first.SetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
                await WaitForMotionFrame(() => label.Text == "已生效" && reload.IsEnabled).ConfigureAwait(true);
                Assert.Equal("已生效，下一轮开始用新的扩展", note.Text);
                if (capture)
                {
                    await WaitForMotionFrame(() => dot.RenderTransform!.Value.M32 is > 0 and < 1.15).ConfigureAwait(true);
                    Capture("ok");
                }

                Click(reload);
                await WaitForMotionFrame(() => attempts == 2 && !reload.IsEnabled).ConfigureAwait(true);
                refused.SetResult(new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent("""{"error":"运行时正在处理任务"}""", Encoding.UTF8, "application/json"),
                });
                await WaitForMotionFrame(() => reload.Classes.Contains("refused")).ConfigureAwait(true);
                Assert.Equal("重载运行时", label.Text);
                Assert.Contains("运行时正在处理任务", note.Text, StringComparison.Ordinal);
                if (capture)
                {
                    await WaitForMotionFrame(() => reload.RenderTransform!.Value.M31 is < -1.1 and > -2.1).ConfigureAwait(true);
                    Capture("bad");
                }

                w.Classes.Remove("motion");
                Assert.Equal(0, reload.RenderTransform!.Value.M31);
                Click(reload);
                await WaitForMotionFrame(() => attempts == 3 && label.Text == "重载中").ConfigureAwait(true);
                Assert.Equal(1, dot.Opacity);
                settled.SetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
                await WaitForMotionFrame(() => label.Text == "已生效").ConfigureAwait(true);
                var expiry = DateTime.UtcNow.AddSeconds(6);
                while (note.IsVisible && DateTime.UtcNow < expiry)
                {
                    await Task.Delay(30).ConfigureAwait(true);
                }

                Assert.False(note.IsVisible);
                Assert.Equal("重载运行时", label.Text);

                void Capture(string phase)
                {
                    if (capture)
                    {
                        (note.IsVisible ? (Control)note : foot).BringIntoView();
                        w.UpdateLayout();
                        CaptureMotionRegion(w, foot, -8, 6, (int)Math.Ceiling(foot.Bounds.Width) + 16, 125, $"runtime-reload-{scheme}-{phase}");
                    }
                }

                static void Click(Button target) => target.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            },
            allowMotion: true);
}
