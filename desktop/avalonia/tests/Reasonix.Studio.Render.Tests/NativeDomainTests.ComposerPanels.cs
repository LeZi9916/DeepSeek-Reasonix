using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Shell.Composer;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks the reference heights and captured content of the composer menus.</summary>
    /// <returns>The menu layout check.</returns>
    [Fact]
    public Task ComposerMenuPanelsUseTheReferenceHeight() => WithComposerPicker((w, reads) =>
    {
        w.RequestedThemeVariant = ThemeVariant.Dark;
        w.AdoptComposer(reads.Read<SessionStatus>("/status"));
        w.FindControl<Button>("PlanMode")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var mode = w.FindControl<ComposerPopup>("ModePopup")!.FindControl<Border>("Frame")!;
        Assert.InRange(mode.Bounds.Height, 152, 156);
        Save(mode, "mode-panel");
        w.FindControl<Button>("ApprovalMode")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var policy = w.FindControl<ComposerPopup>("PolicyPopup")!.FindControl<Border>("Frame")!;
        Assert.InRange(policy.Bounds.Height, 375.5, 376.5);
        Save(policy, "policy-panel");

        void Save(Control control, string name)
        {
            var output = Environment.GetEnvironmentVariable("STUDIO_PARITY_OUTPUT");
            if (string.IsNullOrEmpty(output))
            {
                return;
            }

            Directory.CreateDirectory(output);
            if (control is Avalonia.Controls.Primitives.TemplatedControl templated)
            {
                templated.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1d1e1c"));
            }

            using var image = new RenderTargetBitmap(PixelSize.FromSize(control.Bounds.Size, 1), new Vector(96, 96));
            image.Render(control);
            image.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default);
        }
    });
}
