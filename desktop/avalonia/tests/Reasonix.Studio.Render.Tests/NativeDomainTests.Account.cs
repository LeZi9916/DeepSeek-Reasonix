using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    /// <summary>Separates expired credentials, temporary failure and unread account status.</summary>
    /// <param name="width">The window width.</param><param name="dark">Whether to use the dark theme.</param><returns>The account reads and retry.</returns>
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(760, false)]
    [InlineData(760, true)]
    public Task AccountSettingsKeepsUnreadOfflineAndExpiredStatesDistinct(int width, bool dark) => WithSettings(
        "conversation",
        (w, reads) =>
        {
            var height = width == 1440 ? 900 : 480;
            w.Width = width;
            w.Height = height;
            w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var attempts = 0;
            reads.Exchange = request =>
            {
                if (request.RequestUri!.AbsolutePath != "/account")
                {
                    return Task.FromResult<HttpResponseMessage?>(null);
                }

                if (++attempts == 1)
                {
                    throw new InvalidOperationException("身份服务暂不可用");
                }

                var response = attempts == 2
                    ? """{"signedIn":true,"error":"身份服务暂不可用","user":{"label":"demo","email":"demo@example.com"}}"""
                    : """{"signedIn":false,"expired":true}""";
                return Task.FromResult<HttpResponseMessage?>(JsonReply(JsonNode.Parse(response)!));
            };

            Navigate(w, "settings-account");
            Assert.Contains("无法读取登录状态", VisibleText());
            Assert.DoesNotContain("登录", VisibleText());
            Capture("unread");
            ClickAt(w, "action:account.reload");
            Assert.Contains("demo@example.com", VisibleText());
            Assert.Contains("无法连接身份服务：身份服务暂不可用", VisibleText());
            Capture("offline");
            ClickAt(w, "action:account.reload");
            Assert.Contains("登录", VisibleText());
            Assert.DoesNotContain("demo@example.com", VisibleText());
            Assert.Contains(VisibleText(), text => text?.Contains("已过期", StringComparison.Ordinal) == true);
            Capture("expired");

            string?[] VisibleText() => w.FindControl<ContentControl>("SettingsContent")!.GetVisualDescendants().OfType<TextBlock>()
                .Where(c => c.IsEffectivelyVisible).Select(c => c.Text).ToArray();
            void Capture(string phase) => CaptureFlow(w, $"settings-account-{phase}-{width}x{height}");
        });

    /// <summary>Checks the account card, device progress, confirmed logout and log path through their normal entry points.</summary>
    /// <param name="width">The window width.</param><param name="dark">Whether to use the dark theme.</param><returns>The settings interactions.</returns>
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(760, false)]
    [InlineData(760, true)]
    public Task AccountSettingsUsesDeviceFlowAndShowsHostDiagnostics(int width, bool dark) => WithSettings(
        "conversation",
        (w, reads) =>
        {
            var height = width == 1440 ? 900 : 480;
            w.Width = width;
            w.Height = height;
            w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var signedIn = false;
            var attempts = 0;
            var polls = 0;
            var logouts = 0;
            reads.Exchange = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath switch
            {
                "/account" => JsonReply(JsonNode.Parse(signedIn
                    ? """{"signedIn":true,"user":{"label":"demo","handle":"demo","email":"demo@example.com"}}"""
                    : """{"signedIn":false}""")!),
                "/account/login" => Login(),
                "/account/poll" => Poll(),
                "/account/logout" => Logout(),
                "/studio/logs" => JsonReply(JsonNode.Parse("""
                    {"dir":"C:\\Users\\demo\\AppData\\Roaming\\Reasonix Studio\\io.reasonix.studio.1a2b3c\\logs","crashes":[
                      {"name":"host-20261010T230806Z-2.31.0-4412.log","at":"2026-10-10T23:08:06Z","bytes":8421}
                    ],"truncated":false}
                    """)!),
                _ => null,
            });

            Navigate(w, "settings-account");
            Assert.Contains("社区发帖、崩溃跟进时用；与模型密钥无关", VisibleText());
            Assert.Contains("host-20261010T230806Z-2.31.0-4412.log", string.Join("\n", VisibleText()), StringComparison.Ordinal);
            Capture("signed-out");
            ClickAt(w, "action:account.sign-in");
            Assert.Contains("W7XQ-4TDP", VisibleText());
            Capture("code");
            ClickAt(w, "action:account.cancel-sign-in");
            Assert.Equal(0, polls);
            Assert.DoesNotContain("W7XQ-4TDP", VisibleText());

            ClickAt(w, "action:account.sign-in");
            PumpNavigation(() => VisibleText().Contains("demo@example.com"));
            Assert.Equal(1, polls);
            Capture("signed-in");
            ClickAt(w, "action:account.sign-out");
            Assert.Equal(0, logouts);
            Assert.Equal("确认退出", ControlAt<Button>(w, "action:account.sign-out").Content);
            Capture("confirm");
            ClickAt(w, "action:account.sign-out");
            Assert.Equal(1, logouts);
            Assert.Equal("登录", ControlAt<Button>(w, "action:account.sign-in").Content);

            var scroller = w.FindControl<ScrollViewer>("SettingsScroller")!;
            var logs = w.GetVisualDescendants().OfType<Control>().Single(c => Equals(c.Tag, "about.logs_and_abnormal_exit_records"));
            scroller.Offset = new Vector(0, logs.TranslatePoint(default, scroller)!.Value.Y - 76);
            CaptureFlow(w, $"settings-account-diagnostics-{width}x{height}", resetScroll: false);
            ClickAt(w, "action:account.copy-log-folder");
            var clipboard = w.Clipboard!;
            var pasted = clipboard.TryGetTextAsync().GetAwaiter().GetResult();
            Assert.EndsWith(@"\logs", pasted);

            string?[] VisibleText() => w.FindControl<ContentControl>("SettingsContent")!.GetVisualDescendants().OfType<TextBlock>()
                .Where(c => c.IsEffectivelyVisible).Select(c => c.Text ?? string.Concat(c.Inlines?.OfType<Run>().Select(run => run.Text) ?? [])).ToArray();
            void Capture(string phase) => CaptureFlow(w, $"settings-account-{phase}-{width}x{height}");

            HttpResponseMessage Login()
            {
                attempts++;
                return JsonReply(new JsonObject
                {
                    ["deviceCode"] = "fixture",
                    ["userCode"] = "W7XQ-4TDP",
                    ["verificationUri"] = "https://example.test/device",
                    ["interval"] = attempts == 1 ? 3 : 1,
                    ["expiresIn"] = 20,
                });
            }

            HttpResponseMessage Poll()
            {
                polls++;
                signedIn = true;
                return JsonReply(new JsonObject { ["status"] = "complete" });
            }

            HttpResponseMessage Logout()
            {
                logouts++;
                signedIn = false;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
        });
}
