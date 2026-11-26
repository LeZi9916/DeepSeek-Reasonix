using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Reasonix.Studio.Shell.Workbench;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks the workbench file row's reference card entrance.</summary>
    /// <returns>The file-row entrance check.</returns>
    [Fact]
    public Task WorkbenchFileRowsEnterWithTheReferenceCardMotion() =>
        WithSettingsAsync(
            "conversation",
            async (window, _) =>
            {
                await WaitForSettingsFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
                Navigate(window, "workbench");
                var files = window.FindControl<ListBox>("WorkspaceFiles")!;
                files.ItemsSource = new[] { new WorkspaceFileEntry("motion.txt", false) };
                window.UpdateLayout();
                var row = files.GetVisualDescendants().OfType<WorkspaceFileRow>().Single();
                Assert.IsType<TranslateTransform>(row.RenderTransform);
                await WaitForSettingsFrame(() => row.Opacity == 1 && row.RenderTransform!.Value.M32 == 0).ConfigureAwait(true);
            },
            allowMotion: true);

    /// <summary>Keeps workbench file rows at their final pose when motion is off.</summary>
    /// <returns>The reduced-motion file-row check.</returns>
    [Fact]
    public Task WorkbenchFileRowsHonorReducedMotion() =>
        WithSettingsAsync(
            "conversation",
            (window, _) =>
            {
                Navigate(window, "workbench");
                var row = window.GetVisualDescendants().OfType<WorkspaceFileRow>().First();
                Assert.Equal(1d, row.Opacity);
                Assert.Equal(0d, row.RenderTransform?.Value.M32 ?? 0);
                return Task.CompletedTask;
            });

    /// <summary>Checks the file row's reference 120ms background and ink fades.</summary>
    /// <returns>The file-row hover transition check.</returns>
    [Fact]
    public Task WorkbenchFileRowsFadeHoverBackgroundAndInk() =>
        WithSettingsAsync(
            "conversation",
            async (window, _) =>
            {
                await WaitForSettingsFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
                Navigate(window, "workbench");
                var row = window.GetVisualDescendants().OfType<WorkspaceFileRow>().First();
                var button = row.GetVisualDescendants().OfType<Button>().Single(item => item.Classes.Contains("workspace-file-row"));
                var transitions = button.Transitions!.OfType<BrushTransition>().ToArray();
                Assert.Equal(2, transitions.Length);
                Assert.All(transitions, transition =>
                {
                    Assert.Equal(TimeSpan.FromMilliseconds(120), transition.Duration);
                    Assert.IsType<SplineEasing>(transition.Easing);
                });
            },
            allowMotion: true);
}
