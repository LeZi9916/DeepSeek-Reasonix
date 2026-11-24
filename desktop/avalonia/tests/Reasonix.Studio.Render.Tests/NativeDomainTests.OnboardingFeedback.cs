using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Exercises grid motion and the checking, rejection and discovery feedback.</summary>
    /// <param name="dark">Whether to use the dark theme.</param>
    /// <returns>The setup feedback check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task OnboardingFeedbackTracksProbeAndBackdrop(bool dark) =>
        WithSettingsAsync(
            "onboarding",
            async (w, reads) =>
            {
                w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                await WaitForMotionFrame(() => w.Classes.Contains("motion")).ConfigureAwait(true);
                var scheme = dark ? "dark" : "light";
                var setup = w.FindControl<Grid>("SetupPanel")!;
                var atmosphere = w.GetVisualDescendants().OfType<UserControl>().Single(control => control.Name == "SetupAtmosphere");
                var grid = atmosphere.GetVisualDescendants().OfType<Control>().Single(control => control.Name == "GuideGrid");
                var shift = AvaloniaPropertyRegistry.Instance.FindRegistered(grid, "Shift")!;
                await WaitForMotionFrame(() => (double)grid.GetValue(shift)! >= 1.15).ConfigureAwait(true);
                CaptureMotionRegion(w, setup, 1100, -70, 300, 190, $"onboarding-atmosphere-{scheme}-1000");

                var reply = new TaskCompletionSource<HttpResponseMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
                reads.Exchange = req => req.RequestUri!.AbsolutePath switch
                {
                    "/providers/probe" => reply.Task,
                    "/providers" or "/model" or "/default-model" when req.Method != HttpMethod.Get => Task.FromResult<HttpResponseMessage?>(new HttpResponseMessage(HttpStatusCode.NoContent)),
                    "/provider-setup" => Task.FromResult<HttpResponseMessage?>(JsonReply(new JsonObject { ["required"] = false })),
                    _ => Task.FromResult<HttpResponseMessage?>(null),
                };
                void Click(string button)
                {
                    w.FindControl<Button>(button)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Dispatcher.UIThread.RunJobs();
                }

                var services = w.FindControl<Panel>("SetupServices")!;
                var models = w.FindControl<Panel>("ModelOptions")!;
                var setupCard = w.FindControl<StackPanel>("SetupCard")!;

                async Task CaptureChoices(Panel panel, string step, int height, int lead, double phase)
                {
                    var first = panel.Children[0];
                    var later = panel.Children[lead];
                    await WaitForMotionFrame(() => first.Opacity > later.Opacity && later.Opacity > phase).ConfigureAwait(true);
                    var slide = setupCard.RenderTransform!.Value.M31;
                    CaptureMotionRegion(w, panel, -5 - (int)slide, 5, 560, height, $"onboarding-{step}-cards-{scheme}-95");
                    await WaitForMotionFrame(() => panel.Children.All(choice => !choice.IsAnimating(Visual.OpacityProperty)) &&
                        !setupCard.IsAnimating(Visual.OpacityProperty)).ConfigureAwait(true);
                    CaptureMotionRegion(w, panel, -5, 5, 560, height, $"onboarding-{step}-cards-{scheme}-final");
                }

                w.FindControl<Button>("BeginSetup")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(7, services.Children.Count);
                await CaptureChoices(services, "connect", 146, 2, 0.45).ConfigureAwait(true);
                var key = w.FindControl<TextBox>("ServiceKey")!;
                key.Text = "fixture";
                Dispatcher.UIThread.RunJobs();
                var button = w.FindControl<Button>("ConnectProvider")!;
                Click("ConnectProvider");
                Assert.Contains("checking", button.Classes);
                Assert.False(button.IsEnabled);
                await Task.Delay(350).ConfigureAwait(true);
                CaptureMotionRegion(w, button, -148, 12, 288, 72, $"onboarding-checking-{scheme}-350");

                reply.SetResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent("""{"error":"连接失败（截图夹具）"}"""),
                });
                var error = w.FindControl<TextBlock>("SetupError")!;
                await WaitForMotionFrame(() => error.IsVisible && Math.Abs(key.RenderTransform!.Value.M31) > 1).ConfigureAwait(true);
                CaptureMotionRegion(w, key, -5, 5, 550, 84, $"onboarding-rejected-{scheme}-shake");
                await WaitForMotionFrame(() => button.IsEnabled).ConfigureAwait(true);

                reply = new TaskCompletionSource<HttpResponseMessage?>(TaskCreationOptions.RunContinuationsAsynchronously);
                Click("ConnectProvider");
                reply.SetResult(JsonReply(JsonNode.Parse("""{"kind":"openai","kinds":["openai","responses"],"ambiguous":true,"models":["deepseek-v4-pro","deepseek-flash"],"default":"deepseek-v4-pro","vision":[]}""")!));
                var banner = w.FindControl<Border>("SetupFound")!;
                await WaitForMotionFrame(() => banner.IsVisible && banner.Opacity is > 0 and < 0.99).ConfigureAwait(true);
                Assert.Contains("connected", button.Classes);
                Assert.False(button.IsEnabled);
                CaptureMotionRegion(w, banner, -5, 10, 550, 133, $"onboarding-connected-{scheme}-enter");
                await WaitForMotionFrame(() => !key.IsVisible && button.IsEnabled).ConfigureAwait(true);
                Assert.DoesNotContain("connected", button.Classes);
                Assert.True(banner.IsVisible);
                Assert.Equal(2, models.Children.Count);
                await CaptureChoices(models, "model", 104, 1, 0.8).ConfigureAwait(true);
                var firstModel = Assert.IsType<RadioButton>(models.Children[0]);
                var secondModel = Assert.IsType<RadioButton>(models.Children[1]);
                Assert.True(firstModel.IsChecked);
                secondModel.IsChecked = true;
                Assert.Equal("deepseek-flash", w.FindControl<ComboBox>("ServiceModels")!.SelectedItem);
                firstModel.IsChecked = true;

                Click("ConnectProvider");
                var project = w.FindControl<Control>("ProjectStep")!;
                await WaitForMotionFrame(() => project.IsVisible && project.Opacity == 1).ConfigureAwait(true);
                var drop = project.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "DropZone");
                var stripes = drop.GetVisualDescendants().OfType<Control>().Single(control => control.Name == "DropStripes");
                var stripeShift = AvaloniaPropertyRegistry.Instance.FindRegistered(stripes, "Shift")!;
                var temporary = Directory.CreateTempSubdirectory("reasonix-onboarding-");
                var path = Directory.CreateDirectory(Path.Combine(temporary.FullName, "mock-workspace")).FullName;
                try
                {
                    using var folder = await w.StorageProvider.TryGetFolderFromPathAsync(new Uri(path)).ConfigureAwait(true);
                    Assert.NotNull(folder);
                    using var data = new DataTransfer();
                    data.Add(DataTransferItem.CreateFile(folder));
                    var at = drop.TranslatePoint(new Point(24, 24), w)!.Value;
                    w.DragDrop(at, RawDragEventType.DragEnter, data, DragDropEffects.Copy);
                    w.DragDrop(at, RawDragEventType.DragOver, data, DragDropEffects.Copy);
                    await WaitForMotionFrame(() => stripes.IsVisible && (double)stripes.GetValue(stripeShift)! >= 5).ConfigureAwait(true);
                    CaptureMotionRegion(w, drop, -5, 6, 566, 162, $"onboarding-drop-{scheme}-over");
                    drop.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, data, drop, new Point(24, 24), KeyModifiers.None)
                    {
                        DragEffects = DragDropEffects.Copy,
                    });
                    var chosen = drop.GetVisualDescendants().OfType<Grid>().Single(control => control.Name == "Chosen");
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(chosen.IsVisible, $"Drop did not select {folder!.Path}");
                    Assert.Contains("chosen", drop.Classes);
                    await WaitForMotionFrame(() => chosen.IsVisible && chosen.Opacity is > 0 and < 0.99).ConfigureAwait(true);
                    CaptureMotionRegion(w, drop, -5, 6, 566, 162, $"onboarding-drop-{scheme}-chosen");
                    Assert.False(stripes.IsVisible);
                }
                finally
                {
                    temporary.Delete(recursive: true);
                }

                var composer = w.FindControl<StackPanel>("ComposerArea")!;
                ClickAt(w, "action:onboarding.defer-project");
                await WaitForMotionFrame(() => setup.IsVisible && project.Opacity is > 0 and < 0.93).ConfigureAwait(true);
                CaptureMotionRegion(w, setup, 580, -280, 450, 155, $"onboarding-departure-{scheme}-leave");
                await WaitForMotionFrame(() => !setup.IsVisible && composer.Opacity is > 0 and < 0.98).ConfigureAwait(true);
                CaptureMotionRegion(w, w.FindControl<Grid>("LayoutRoot")!, 475, -740, 480, 150, $"onboarding-departure-{scheme}-rise");
                await WaitForMotionFrame(() => composer.Opacity == 1).ConfigureAwait(true);
            },
            allowMotion: true);
}
