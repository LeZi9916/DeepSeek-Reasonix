using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Shell;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>An attachment token becomes a chip that removes itself.</summary>
    /// <returns>The chip interaction.</returns>
    [Fact]
    public Task AttachmentChipAppearsAndCanBeRemoved() => WithSettings(
        "conversation",
        (w, _) =>
        {
            var add = typeof(MainWindow).GetMethod("AddAttachments", BindingFlags.Instance | BindingFlags.NonPublic)!;
            add.Invoke(w, [new[] { new DroppedReference("@shot.png", "shot.png", null) }]);
            Dispatcher.UIThread.RunJobs();
            var strip = w.FindControl<WrapPanel>("AttachmentStrip")!;
            var area = w.FindControl<StackPanel>("AttachmentArea")!;
            Assert.True(area.IsVisible);
            Assert.Contains("shot.png", strip.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));

            var remove = strip.GetVisualDescendants().OfType<Button>().First(button => button.Classes.Contains("attachment-remove"));
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(area.IsVisible);
        });

    /// <summary>A quote chip reaches the model as a reference block, not as typed text.</summary>
    /// <returns>The submitted quote.</returns>
    [Fact]
    public Task QuoteChipTravelsInTheSubmitText() => WithSettings(
        "conversation",
        (w, _) =>
        {
            var add = typeof(MainWindow).GetMethod("AddQuoteChip", BindingFlags.Instance | BindingFlags.NonPublic)!;
            add.Invoke(w, ["selected words", "notes.md"]);
            var compose = typeof(MainWindow).GetMethod("ComposeSubmitText", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var submit = Assert.IsType<string>(compose.Invoke(w, ["ask"]));
            Assert.Contains("[Quoting text selected in the workspace file notes.md.]", submit);
            Assert.Contains("> selected words", submit);
            Assert.EndsWith("ask", submit);
            Assert.True(string.IsNullOrEmpty(w.FindControl<TextBox>("Composer")!.Text));
        });

    /// <summary>A refused drop keeps its reason and a retry action.</summary>
    /// <returns>The failure chip.</returns>
    [Fact]
    public Task AttachmentFailureKeepsReasonAndRetry() => WithSettings(
        "conversation",
        (w, _) =>
        {
            var add = typeof(MainWindow).GetMethod("AddAttachments", BindingFlags.Instance | BindingFlags.NonPublic)!;
            add.Invoke(w, [new[] { new DroppedReference(null, "bad.txt", "no access") }]);
            Dispatcher.UIThread.RunJobs();
            var strip = w.FindControl<WrapPanel>("AttachmentStrip")!;
            Assert.Contains("no access", strip.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
            Assert.Contains(strip.GetVisualDescendants().OfType<Button>(), button => button.Classes.Contains("attachment-retry"));
        });

    /// <summary>A picture with a model that does not read images shows the reference warning.</summary>
    /// <returns>The warning state.</returns>
    [Fact]
    public Task AttachmentPictureWarnsWhenTheModelCannotRead() => WithSettings(
        "conversation",
        (w, _) =>
        {
            var show = typeof(MainWindow).GetMethod("ShowComposeStatus", BindingFlags.Instance | BindingFlags.NonPublic)!;
            show.Invoke(w, [new SessionStatus { Vision = false }]);
            var add = typeof(MainWindow).GetMethod("AddAttachments", BindingFlags.Instance | BindingFlags.NonPublic)!;
            add.Invoke(w, [new[] { new DroppedReference("@shot.png", "shot.png", null) }]);
            Dispatcher.UIThread.RunJobs();
            var warning = w.FindControl<TextBlock>("AttachmentWarning")!;
            Assert.True(warning.IsVisible);
            Assert.Contains("不支持读图", warning.Text);
        });
}
