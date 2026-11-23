using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using FileEntry = Reasonix.Studio.Shell.Workbench.WorkspaceFileEntry;

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

    /// <summary>A failed file read stays in the canvas and offers read again or close.</summary>
    /// <returns>The recovery interaction.</returns>
    [Fact]
    public Task WorkbenchFileReadFailureOffersRecovery() => WithSettings(
        "workbench",
        (w, reads) =>
        {
            reads.Exchange = request => Task.FromResult<HttpResponseMessage?>(
                request.RequestUri!.AbsolutePath == "/workspace/file"
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : request.RequestUri.AbsolutePath == "/workspace/files" && request.RequestUri.Query.Contains("path=internal", StringComparison.Ordinal)
                        ? JsonReply(new JsonObject { ["files"] = new JsonArray("internal/config.go"), ["directories"] = new JsonArray() })
                        : null);
            var listing = w.FindControl<ListBox>("WorkspaceFiles")!;
            listing.GetVisualDescendants().OfType<Button>().First(button => AutomationProperties.GetName(button) == "internal")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            listing.SelectedItem = listing.Items.OfType<FileEntry>().First(item => item.Path == "internal/config.go");
            Assert.True(SpinWait.SpinUntil(
                () =>
                {
                    Dispatcher.UIThread.RunJobs();
                    return w.FindControl<Border>("FileErrorPanel")!.IsVisible;
                },
                System.TimeSpan.FromSeconds(2)));
            reads.Exchange = _ => Task.FromResult<HttpResponseMessage?>(null);
            w.FindControl<Button>("FileErrorRetry")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(SpinWait.SpinUntil(
                () =>
                {
                    Dispatcher.UIThread.RunJobs();
                    return !w.FindControl<Border>("FileErrorPanel")!.IsVisible;
                },
                System.TimeSpan.FromSeconds(2)));
        });

    /// <summary>A failed Git inventory offers the same read again.</summary>
    /// <returns>The retry interaction.</returns>
    [Fact]
    public Task WorkbenchGitInventoryFailureOffersRetry() => WithSettings(
        "workbench",
        (w, reads) =>
        {
            reads.Exchange = request => Task.FromResult<HttpResponseMessage?>(
                request.RequestUri!.AbsolutePath == "/git/repos"
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : null);
            var retry = w.FindControl<Button>("GitRetry")!;
            retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(SpinWait.SpinUntil(
                () =>
                {
                    Dispatcher.UIThread.RunJobs();
                    return retry.IsVisible;
                },
                System.TimeSpan.FromSeconds(2)));
            reads.Exchange = _ => Task.FromResult<HttpResponseMessage?>(null);
            retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(SpinWait.SpinUntil(
                () =>
                {
                    Dispatcher.UIThread.RunJobs();
                    return !retry.IsVisible;
                },
                System.TimeSpan.FromSeconds(2)));
        });

    /// <summary>The file editor quotes a selection into the composer instead of replacing it.</summary>
    /// <returns>The text action.</returns>
    [Fact]
    public Task WorkbenchSelectionQuotesIntoTheComposer() => WithSettings(
        "workbench",
        (w, _) =>
        {
            var editor = w.FindControl<TextBox>("FileEditor")!;
            editor.Text = "first line\nsecond line";
            editor.SelectionStart = 0;
            editor.SelectionEnd = "first line".Length;
            var menu = Assert.IsType<ContextMenu>(editor.ContextMenu);
            menu.Open(editor);
            Dispatcher.UIThread.RunJobs();
            var quote = Assert.IsType<MenuItem>(menu.Items[0]);
            Assert.True(quote.IsEnabled);
            quote.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(w.FindControl<StackPanel>("AttachmentArea")!.IsVisible);
            Assert.Contains(
                "引用 README.md",
                w.FindControl<WrapPanel>("AttachmentStrip")!.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
        });

    /// <summary>The explorer exposes its visible node count at the reference width.</summary>
    /// <returns>The explorer chrome.</returns>
    [Fact]
    public Task WorkbenchExplorerShowsCountAndDefaultWidth() => WithSettings(
        "workbench",
        (w, _) =>
        {
            var pane = w.FindControl<Grid>("ExplorerPane")!;
            Assert.Equal(190, pane.Bounds.Width);
            Assert.False(string.IsNullOrWhiteSpace(w.FindControl<TextBlock>("ExplorerCount")!.Text));
            Assert.NotNull(w.FindControl<Avalonia.Controls.Primitives.Thumb>("ExplorerGrip"));
        });

    /// <summary>The explorer's clear control empties the query and restores the listing.</summary>
    /// <returns>The search interaction.</returns>
    [Fact]
    public Task WorkbenchClearSearchRestoresTheListing() => WithSettings(
        "workbench",
        (w, _) =>
        {
            var search = w.FindControl<TextBox>("WorkbenchSearch")!;
            var clear = w.FindControl<Button>("ClearWorkbenchSearch")!;
            Assert.False(clear.IsVisible);
            search.Text = "README";
            Dispatcher.UIThread.RunJobs();
            Assert.True(clear.IsVisible);
            clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(string.Empty, search.Text);
            Assert.False(clear.IsVisible);
            Assert.NotEmpty(w.FindControl<ListBox>("WorkspaceFiles")!.Items);
        });

    /// <summary>The sidebar's Files shortcut opens the same workbench as the view menu.</summary>
    /// <returns>The navigation interaction.</returns>
    [Fact]
    public Task SidebarFilesOpensTheWorkbench() => WithSettings(
        "conversation",
        (w, _) =>
        {
            w.FindControl<Button>("OpenAccount")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var shortcuts = w.FindControl<Avalonia.Controls.Primitives.Popup>("QuickLinksPopup")!;
            Assert.True(shortcuts.IsOpen);
            var files = w.FindControl<StackPanel>("QuickLinksRows")!.Children.OfType<Button>()
                .Single(button => AutomationProperties.GetAutomationId(button) == "action:files.open");
            files.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(shortcuts.IsOpen);
            var opened = SpinWait.SpinUntil(
                () =>
                {
                    Dispatcher.UIThread.RunJobs();
                    return w.FindControl<Border>("WorkbenchPanel")!.IsVisible;
                },
                System.TimeSpan.FromSeconds(2));
            Assert.True(opened);
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
            var view = ControlAt<Control>(w, "class:workbench-diff");
            var text = Assert.Single(view.GetVisualDescendants().OfType<SelectableTextBlock>());
            Assert.Equal("@@ -1 +1 @@oldnew", string.Concat(text.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)));
            var markers = view.GetVisualDescendants().OfType<TextBlock>().Where(block => block.Classes.Contains("diff-sign")).ToArray();
            Assert.Equal(new[] { string.Empty, "-", "+" }, markers.Select(block => block.Text));
            Assert.Equal(3, markers.Select(block => block.Foreground).Distinct().Count());
        });
}
