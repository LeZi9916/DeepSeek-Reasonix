using Avalonia;
using Avalonia.Controls;
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
using FileEntry = Reasonix.Studio.Shell.Workbench.WorkspaceFileEntry;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Drives the display setting and the workspace file's normal preview read.</summary>
    /// <param name="width">The viewport width.</param><param name="dark">Whether to use the dark theme.</param>
    /// <returns>The settings and Office preview interaction.</returns>
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(760, false)]
    [InlineData(760, true)]
    public Task OfficePreviewSettingControlsTheWorkbenchRead(int width, bool dark) => WithSettings(
        "workbench",
        (w, reads) =>
        {
            var height = width == 1440 ? 900 : 480;
            w.Width = width;
            w.Height = height;
            w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var requests = 0;
            var external = 0;
            reads.Exchange = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath switch
            {
                "/workspace/files" => JsonReply(new JsonObject { ["files"] = new JsonArray("sales.xlsx"), ["directories"] = new JsonArray() }),
                "/workspace/preview/info" => JsonReply(new JsonObject { ["kind"] = "office", ["version"] = "v1", ["size"] = 7340, ["limit"] = 33554432 }),
                "/workspace/preview/content" => Bytes(),
                "/workspace/open-document" => External(),
                _ => null,
            });
            Navigate(w, "settings-display");
            var option = ControlAt<ToggleSwitch>(w, "action:appearance.office-preview");
            Assert.True(option.IsChecked);
            var scroller = w.FindControl<ScrollViewer>("SettingsScroller")!;
            scroller.Offset = new Vector(0, scroller.Extent.Height);
            CaptureFlow(w, $"settings-office-setting-on-{width}x{height}", resetScroll: false);
            option.IsChecked = false;
            CaptureFlow(w, $"settings-office-setting-off-{width}x{height}", resetScroll: false);
            ClickAt(w, "action:settings.close");

            w.FindControl<Button>("FilesTab")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var files = w.FindControl<ListBox>("WorkspaceFiles")!;
            Dispatcher.UIThread.RunJobs();
            files.SelectedItem = files.Items.OfType<FileEntry>().Single(item => item.Path == "sales.xlsx");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, requests);
            Assert.Contains(w.FindControl<StackPanel>("FileTabs")!.GetVisualDescendants().OfType<Button>(), tab => Equals(tab.Content, "sales.xlsx") && tab.Classes.Contains("active"));
            w.FindControl<Control>("ExplorerPane")!.IsVisible = false;
            CaptureFlow(w, $"settings-office-preview-off-{width}x{height}");
            ClickAt(w, "action:workbench.open-office-external");
            Assert.Equal(1, external);
            ClickAt(w, "action:workbench.enable-office-preview");
            option = ControlAt<ToggleSwitch>(w, "action:appearance.office-preview");
            Assert.False(option.IsChecked);
            option.IsChecked = true;
            ClickAt(w, "action:settings.close");
            PumpNavigation(() => requests == 1);

            HttpResponseMessage Bytes()
            {
                requests++;
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[12]) };
                response.Headers.Add("X-Preview-Version", "v1");
                return response;
            }

            HttpResponseMessage External()
            {
                external++;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
        });
}
