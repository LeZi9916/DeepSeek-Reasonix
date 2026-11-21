using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks the grouped signal rail, its members and zoom reset.</summary>
    /// <returns>The analysis interaction.</returns>
    [Fact]
    public Task AnalysisGroupsSignalsAndResetsZoom() => WithSettings(
        "analysis",
        (w, _) =>
        {
            var zoom = ControlAt<Slider>(w, "action:analysis.zoom");
            Assert.Equal(16, zoom.Maximum);
            ClickAt(w, "action:analysis.signal");
            ClickAt(w, "action:analysis.signal-member");
            zoom.Value = 8;
            ClickAt(w, "action:analysis.reset-zoom");
            Assert.Equal(1, zoom.Value);
        });

    /// <summary>Selects a clipped file tab through the overflow menu.</summary>
    /// <returns>The workbench interaction.</returns>
    [Fact]
    public Task WorkbenchOverflowSelectsHiddenFiles() => WithSettings(
        "workbench",
        (w, reads) =>
        {
            var files = Enumerable.Range(1, 7).Select(n => "workspace-document-with-a-long-name-" + n + ".md").ToArray();
            reads.Exchange = req =>
            {
                var uri = req.RequestUri!;
                if (uri.AbsolutePath == "/workspace/files")
                {
                    return Task.FromResult<System.Net.Http.HttpResponseMessage?>(JsonReply(new JsonObject { ["files"] = System.Text.Json.JsonSerializer.SerializeToNode(files), ["directories"] = new JsonArray() }));
                }

                if (uri.AbsolutePath == "/workspace/file")
                {
                    var file = reads.Read<JsonObject>("/workspace/file");
                    file["path"] = System.Uri.UnescapeDataString(uri.Query[6..]);
                    return Task.FromResult<System.Net.Http.HttpResponseMessage?>(JsonReply(file));
                }

                return Task.FromResult<System.Net.Http.HttpResponseMessage?>(null);
            };
            w.FindControl<Button>("FilesTab")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var listing = w.FindControl<ListBox>("WorkspaceFiles")!;
            foreach (var name in files)
            {
                listing.SelectedItem = listing.Items.Cast<object>().Single(item => item.ToString() == name);
                Dispatcher.UIThread.RunJobs();
            }

            var overflow = ControlAt<Button>(w, "action:workbench.tab-overflow");
            overflow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var menu = Assert.IsType<MenuFlyout>(overflow.Flyout);
            var first = Assert.IsType<MenuItem>(menu.Items[0]);
            first.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(first.Tag, w.FindControl<TextBlock>("WorkbenchTitle")!.Text);
        });

    /// <summary>Opens a real file's Diff view and checks classified, selectable content.</summary>
    /// <returns>The workbench interaction.</returns>
    [Fact]
    public Task WorkbenchShowsUnifiedDiff() => WithSettings(
        "workbench",
        (w, reads) =>
        {
            var patch = "@@ -1 +1 @@\n-old\n+new\n";
            reads.Exchange = req => Task.FromResult(req.RequestUri!.AbsolutePath == "/changes/diff" ? JsonReply(new JsonObject { ["diff"] = patch }) : null);
            w.FindControl<Button>("DiffFile")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var text = ControlAt<SelectableTextBlock>(w, "class:workbench-diff");
            Assert.Equal(patch, string.Concat(text.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)));
            Assert.True(text.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Foreground).Distinct().Count() >= 3);
        });
}
