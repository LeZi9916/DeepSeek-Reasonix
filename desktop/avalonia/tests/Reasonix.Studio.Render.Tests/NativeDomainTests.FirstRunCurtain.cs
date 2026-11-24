using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Styling;
using Reasonix.Studio.Shell.Startup;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Captures the old short welcome phases and verifies its natural completion.</summary>
    /// <param name="dark">Whether the workspace underneath uses the dark theme.</param>
    /// <returns>The first-run integration check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FirstRunCurtainFollowsReferenceTimeline(bool dark) => WithSettingsAsync(
        "conversation",
        async (window, reads) =>
        {
            window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            await WaitForMotionFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
            var writes = 0;
            reads.Exchange = request =>
            {
                if (request.RequestUri?.AbsolutePath == "/welcome" && request.Method == HttpMethod.Post)
                {
                    writes++;
                }

                return FirstRunReply(request, writes > 0, needsSetup: false);
            };
            var curtain = window.FindControl<WelcomeCurtain>("FirstRunCurtain")!;
            await window.LoadStartupPreferencesAsync().ConfigureAwait(true);
            Assert.True(curtain.IsVisible);
            Assert.Equal(0, writes);
            var scheme = dark ? "dark" : "light";
            foreach (var phase in new[] { 600, 2100, 4580, 6120 })
            {
                await WaitForMotionFrame(() => curtain.SceneTime >= phase).ConfigureAwait(true);
                CaptureMotionRegion(window, curtain, 430, -220, 580, 475, $"first-run-curtain-{scheme}-{phase}");
                if (phase == 4580)
                {
                    CaptureMotionRegion(window, curtain, 620, -830, 820, 65, $"first-run-progress-{scheme}-4580");
                }
            }

            await WaitForMotionFrame(() => curtain.LeaveTime >= 110).ConfigureAwait(true);
            CaptureMotionRegion(window, curtain, 430, -220, 580, 475, $"first-run-curtain-{scheme}-leave");
            await WaitForMotionFrame(() => !curtain.IsVisible && writes == 1).ConfigureAwait(true);
            await window.LoadStartupPreferencesAsync().ConfigureAwait(true);
            Assert.False(curtain.IsVisible);
            Assert.Equal(1, writes);
        },
        allowMotion: true);

    /// <summary>Verifies that the reference shortcut dismisses the curtain without reaching the timer.</summary>
    /// <param name="keyboard">Whether to use the keyboard instead of clicking the curtain.</param>
    /// <returns>The first-run skip integration check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FirstRunCurtainHonorsPointerAndKeySkip(bool keyboard) => WithSettingsAsync(
        "conversation",
        async (window, reads) =>
        {
            await WaitForMotionFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
            var writes = 0;
            reads.Exchange = request =>
            {
                if (request.RequestUri?.AbsolutePath == "/welcome" && request.Method == HttpMethod.Post)
                {
                    writes++;
                }

                return FirstRunReply(request, writes > 0, needsSetup: false);
            };
            var curtain = window.FindControl<WelcomeCurtain>("FirstRunCurtain")!;
            await window.LoadStartupPreferencesAsync().ConfigureAwait(true);
            await WaitForMotionFrame(() => curtain.SceneTime > 50).ConfigureAwait(true);
            Assert.Null(curtain.LeaveTime);
            if (keyboard)
            {
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            }
            else
            {
                window.MouseDown(new Point(700, 450), MouseButton.Left);
            }

            await WaitForMotionFrame(() => !curtain.IsVisible && writes == 1).ConfigureAwait(true);
            Assert.InRange(curtain.SceneTime, 380, 3000);
        },
        allowMotion: true);

    /// <summary>Verifies the reduced-motion branch and prioritizes provider setup when required.</summary>
    /// <param name="seen">Whether the kernel has already stored the welcome.</param>
    /// <param name="needsSetup">Whether the configured provider requires onboarding.</param>
    /// <returns>The short-circuit integration check.</returns>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public Task FirstRunCurtainUsesTheKernelMarkerAndSetupGate(bool seen, bool needsSetup) => WithSettingsAsync(
        "conversation",
        async (window, reads) =>
        {
            var writes = 0;
            reads.Exchange = request =>
            {
                if (request.RequestUri?.AbsolutePath == "/welcome" && request.Method == HttpMethod.Post)
                {
                    writes++;
                }

                return FirstRunReply(request, seen || writes > 0, needsSetup);
            };
            await window.LoadStartupPreferencesAsync().ConfigureAwait(true);
            var curtain = window.FindControl<WelcomeCurtain>("FirstRunCurtain")!;
            var setup = window.FindControl<Grid>("SetupPanel")!;
            if (seen)
            {
                Assert.False(curtain.IsVisible);
                Assert.False(setup.IsVisible);
                Assert.Equal(0, writes);
                return;
            }

            if (needsSetup)
            {
                Assert.True(setup.IsVisible);
                Assert.False(curtain.IsVisible);
                Assert.Equal(0, writes);
                ClickAt(window, "action:onboarding.skip");
            }
            else
            {
                await WaitForMotionFrame(() => !curtain.IsVisible && writes == 1).ConfigureAwait(true);
                Assert.InRange(curtain.SceneTime, 380, 2000);
            }

            Assert.False(setup.IsVisible);
            Assert.Equal(1, writes);
        });

    /// <summary>Serves the two kernel-owned startup decisions in the session fixture.</summary>
    /// <param name="request">The outgoing page request.</param>
    /// <param name="seen">Whether the kernel remembers a completed welcome.</param>
    /// <param name="needsSetup">Whether the provider needs configuration.</param>
    /// <returns>The supplied response or the fixture's default.</returns>
    private static Task<HttpResponseMessage?> FirstRunReply(HttpRequestMessage request, bool seen, bool needsSetup)
    {
        HttpResponseMessage? reply = request.RequestUri?.AbsolutePath switch
        {
            "/welcome" when request.Method == HttpMethod.Post => new(HttpStatusCode.NoContent),
            "/welcome" => JsonReply(new JsonObject { ["seen"] = seen }),
            "/provider-setup" => JsonReply(new JsonObject { ["required"] = needsSetup, ["noProvider"] = needsSetup }),
            _ => null,
        };
        return Task.FromResult(reply);
    }
}
