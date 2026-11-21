using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks the file row's reference geometry, icons and neutral selection in both themes.</summary>
    /// <param name="width">Window width.</param>
    /// <param name="dark">Requested palette.</param>
    /// <returns>The rendered file tree.</returns>
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(760, false)]
    [InlineData(760, true)]
    public Task FileTreeUsesReferenceRows(int width, bool dark) => WithSettings("workbench", (w, _) =>
    {
        w.Width = width;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();
        var list = w.FindControl<ListBox>("WorkspaceFiles")!;
        var rows = list.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("workspace-file-row")).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal(new[] { "internal", "README.md" }, rows.Select(b => b.GetVisualDescendants().OfType<TextBlock>().First().Text));
        Assert.All(rows, row =>
        {
            Assert.Equal(30, row.Bounds.Height);
            Assert.Equal(12, row.FontSize);
            Assert.Equal(FontWeight.Medium, row.FontWeight);
            Assert.Equal(new Thickness(8, 0, 10, 0), row.Padding);
            Assert.Contains(row.GetVisualDescendants(), c => c is Avalonia.Controls.Shapes.Path);
        });
        var selected = list.GetVisualDescendants().OfType<ListBoxItem>().Single(c => c.IsSelected);
        Assert.Equal(new Thickness(0), selected.Padding);
        Assert.True(w.TryFindResource("OverlayBrush", w.ActualThemeVariant, out var overlay));
        Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(overlay).Color, Assert.IsAssignableFrom<ISolidColorBrush>(selected.Background).Color);
    });

    /// <summary>Expands folders in place and preserves them across status refreshes and search.</summary>
    /// <returns>The file-tree interaction.</returns>
    [Fact]
    public Task FileTreeExpandsWithoutLosingSiblings() => WithSettings("workbench", (w, reads) =>
    {
        reads.Exchange = request =>
        {
            if (request.RequestUri!.AbsolutePath != "/workspace/files")
            {
                return Task.FromResult<System.Net.Http.HttpResponseMessage?>(null);
            }

            var query = request.RequestUri.Query;
            var value = query switch
            {
                _ when query.Contains("q=absent", StringComparison.Ordinal) => """{"files":[],"directories":[]}""",
                _ when query.Contains("q=retry", StringComparison.Ordinal) || query.Contains("path=internal%2Fprovider", StringComparison.OrdinalIgnoreCase) => """{"files":["internal/provider/retry.go"],"directories":[]}""",
                _ when query.Contains("path=internal", StringComparison.Ordinal) => """{"files":["internal/config.go"],"directories":["internal/provider"]}""",
                _ => """{"files":["README.md"],"directories":["internal"]}""",
            };
            return Task.FromResult<System.Net.Http.HttpResponseMessage?>(JsonReply(JsonNode.Parse(value)!));
        };
        w.FindControl<Button>("FilesTab")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var list = w.FindControl<ListBox>("WorkspaceFiles")!;
        void Click(string name)
        {
            var row = list.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == name);
            row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        string[] Names() => list.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("workspace-file-row")).Select(AutomationProperties.GetName).ToArray()!;
        Click("internal");
        Assert.Equal(new[] { "internal", "internal/provider", "internal/config.go", "README.md" }, Names());
        Click("internal/provider");
        Assert.Contains("internal/provider/retry.go", Names());
        w.FindControl<Button>("FilesTab")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("internal/provider/retry.go", Names());
        Click("internal");
        Assert.Equal(new[] { "internal", "README.md" }, Names());
        list.SelectedItem = list.Items.Cast<object>().Single(item => item.ToString() == "internal");
        list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("internal/config.go", Names());
        list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Left });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "internal", "README.md" }, Names());
        Assert.Equal("README.md", w.FindControl<TextBlock>("WorkbenchTitle")!.Text);
        var search = w.FindControl<TextBox>("WorkbenchSearch")!;
        search.Text = "retry";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "internal", "internal/provider", "internal/provider/retry.go" }, Names());
        Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(), b => b.Text == "M");
        search.Text = string.Empty;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "internal", "README.md" }, Names());
        search.Text = "absent";
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(Names());
        Assert.True(w.FindControl<TextBlock>("NoWorkspaceFiles")!.IsEffectivelyVisible);
        w.FindControl<Button>("ToggleFileList")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.FindControl<TextBlock>("NoWorkspaceFiles")!.IsEffectivelyVisible);
    });
}
