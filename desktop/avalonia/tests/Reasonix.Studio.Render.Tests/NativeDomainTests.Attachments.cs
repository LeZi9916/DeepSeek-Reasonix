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
            Assert.True(strip.IsVisible);
            Assert.Contains("shot.png", strip.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));

            var remove = strip.GetVisualDescendants().OfType<Button>().First(button => button.Classes.Contains("attachment-remove"));
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(strip.IsVisible);
        });
}
