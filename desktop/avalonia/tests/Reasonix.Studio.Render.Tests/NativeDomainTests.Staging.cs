using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks native three-state semantics and centered marks in both palettes and viewport sizes.</summary>
    /// <param name="width">Window width.</param>
    /// <param name="dark">Requested palette.</param>
    /// <returns>The staging controls.</returns>
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(760, false)]
    [InlineData(760, true)]
    public Task StageBoxesUseCenteredThreeStateMarks(int width, bool dark) => WithSettings("workbench", (w, _) =>
    {
        w.Width = width;
        w.Height = width == 760 ? 480 : 900;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();
        var buttons = w.GetVisualDescendants().OfType<Button>().Where(b => AutomationProperties.GetAutomationId(b) is "action:workbench.stage" or "action:workbench.stage-folder").ToArray();
        Assert.NotEmpty(buttons);
        var boxes = buttons.Select(Assert.IsAssignableFrom<CheckBox>).ToArray();
        Assert.Contains(boxes, box => box.IsChecked == true);
        Assert.Contains(boxes, box => box.IsChecked == false);
        Assert.Contains(boxes, box => box.IsChecked is null);
        foreach (var box in boxes)
        {
            Assert.Equal(new Size(16, 16), box.Bounds.Size);
            Assert.Equal(Avalonia.Layout.VerticalAlignment.Center, box.VerticalAlignment);
            Assert.Null(box.Content);
            Assert.True(box.IsThreeState);
            var peer = ControlAutomationPeer.CreatePeerForElement(box)!;
            Assert.Equal(AutomationControlType.CheckBox, peer.GetAutomationControlType());
            var state = box.IsChecked switch
            {
                true => ToggleState.On,
                false => ToggleState.Off,
                null => ToggleState.Indeterminate,
            };
            Assert.Equal(state, Assert.IsAssignableFrom<IToggleProvider>(peer).ToggleState);
            var row = Assert.IsType<Grid>(box.Parent);
            Assert.InRange(Math.Abs(box.Bounds.Center.Y - (row.Bounds.Height / 2)), 0, 0.5);
            if (box.IsChecked is false)
            {
                continue;
            }

            var mark = box.GetVisualDescendants().OfType<Control>().Single(c => c.Name == (box.IsChecked == true ? "PART_Check" : "PART_Mixed"));
            Assert.True(mark.IsEffectivelyVisible);
            var point = mark.TranslatePoint(default, box)!.Value;
            Assert.InRange(Math.Abs(point.X + (mark.Bounds.Width / 2) - 8), 0, 0.5);
            Assert.InRange(Math.Abs(point.Y + (mark.Bounds.Height / 2) - 8), 0, 0.5);
        }

        var selected = boxes.First(box => box.IsChecked == true);
        var original = Assert.IsAssignableFrom<ISolidColorBrush>(selected.Background).Color;
        selected.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        w.MouseMove(selected.TranslatePoint(new Point(8, 8), w)!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.True(selected.IsPointerOver);
        Assert.Equal(original, Assert.IsAssignableFrom<ISolidColorBrush>(selected.Background).Color);
        Assert.Equal(Color.Parse(dark ? "#E7E8E3" : "#292B29"), Assert.IsAssignableFrom<ISolidColorBrush>(selected.BorderBrush).Color);
        Assert.True(selected.Focus(NavigationMethod.Tab));
        Assert.NotNull(selected.FocusAdorner);
    });

    /// <summary>Accessibility activation requests a Git mutation and waits for its authoritative state.</summary>
    /// <returns>The staging and unstaging interaction.</returns>
    [Fact]
    public Task StageBoxesFollowHostReadback() => WithSettings("workbench", (w, reads) =>
    {
        var listing = reads.Read<JsonObject>("/changes");
        var change = listing["changes"]!.AsArray().First(c => (bool?)c!["staged"] != true)!;
        var path = (string)change["path"]!;
        var writes = new System.Collections.Generic.List<bool>();
        reads.Exchange = async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/changes")
            {
                return JsonReply(listing);
            }

            if (request.RequestUri.AbsolutePath == "/changes/stage" && request.Method == HttpMethod.Post)
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
                Assert.Equal(path, (string?)body["paths"]?[0]);
                var unstage = (bool?)body["unstage"] == true;
                writes.Add(unstage);
                change["staged"] = !unstage;
                change["unstaged"] = unstage;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return null;
        };
        CheckBox Box() => w.GetVisualDescendants().OfType<CheckBox>().Single(box => AutomationProperties.GetAutomationId(box) == "action:workbench.stage" && AutomationProperties.GetName(box)!.EndsWith(path, StringComparison.Ordinal));
        var original = Box();
        Assert.False(original.IsChecked);
        Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(original)).Toggle();
        Dispatcher.UIThread.RunJobs();
        Assert.False(original.IsChecked);
        var current = Box();
        Assert.True(current.IsChecked);
        Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(current)).Toggle();
        Dispatcher.UIThread.RunJobs();
        Assert.True(current.IsChecked);
        Assert.False(Box().IsChecked);
        Assert.Equal(new[] { false, true }, writes);
    });
}
