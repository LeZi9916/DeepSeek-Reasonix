using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.Rail;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Project folds, pagination and scope changes retain canonical row identities.</summary>
    /// <returns>The sidebar interaction.</returns>
    [Fact]
    public Task SidebarFoldsAndFiltersWithoutLosingSessions() => WithSettings("conversation", (w, reads) =>
    {
        var rail = w.FindControl<ProjectRail>("Projects")!;
        var tree = reads.Read<TreeWorkspace[]>("/tree");
        rail.Show(tree, null);
        Dispatcher.UIThread.RunJobs();
        Button[] Actions(string id) => rail.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && (AutomationProperties.GetAutomationId(b) ?? b.Tag as string) == id).ToArray();
        Assert.Equal(10, Actions("action:session.open").Length);
        Actions("action:session.more")[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(11, Actions("action:session.open").Length);
        var scope = Actions("action:session.scope")[0];
        var menu = Assert.IsType<MenuFlyout>(scope.Flyout);
        var unread = menu.Items.OfType<MenuItem>().Single(i => AutomationProperties.GetAutomationId(i) == "action:session.scope.unread");
        unread.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(Actions("action:session.open"));
        Actions("action:session.view")[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(11, Actions("action:session.open").Length);
        Actions("action:workspace.fold")[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(5, Actions("action:session.open").Length);
        rail.Show(tree, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(5, Actions("action:session.open").Length);
        Actions("action:workspace.fold-all")[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(Actions("action:session.open"));
        Actions("action:workspace.fold-all")[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(11, Actions("action:session.open").Length);
    });

    /// <summary>Uses the reference's measured rows, embedded faces and semantic foregrounds.</summary>
    /// <param name="dark">Requested appearance.</param>
    /// <returns>The mounted sidebar.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SidebarUsesReferenceRowsAndTypography(bool dark) => WithSettings("conversation", (w, reads) =>
    {
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        w.FindControl<ProjectRail>("Projects")!.Show(reads.Read<TreeWorkspace[]>("/tree"), null);
        Dispatcher.UIThread.RunJobs();
        var labels = w.FindControl<ProjectRail>("Projects")!.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("session-title")).ToArray();
        Assert.Equal(10, labels.Length);
        foreach (var label in labels)
        {
            Assert.Equal(13, label.FontSize);
            Assert.Equal(FontWeight.Medium, label.FontWeight);
            Assert.Equal(Color.Parse(dark ? "#B4B7B0" : "#5E625C"), Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color);
            Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(label.FontFamily, label.FontStyle, label.FontWeight), out var face));
            Assert.Equal("Reasonix Workbench UI", face.FamilyName);
        }

        Assert.Equal(30, ControlAt<Button>(w, "action:workspace.fold").Bounds.Height);
        Assert.Equal(30, ControlAt<Button>(w, "action:session.open").Bounds.Height);
        Assert.Contains(w.FindControl<Button>("OpenSettings")!.GetVisualDescendants(), c => c is Avalonia.Controls.Shapes.Path);
    });

    /// <summary>Scopes and session menus use canonical unread, pin and archive state.</summary>
    /// <returns>The row actions and footer navigation.</returns>
    [Fact]
    public Task SidebarScopesAndMenusUseCanonicalState() => WithSettings("conversation", (w, reads) =>
    {
        var rail = w.FindControl<ProjectRail>("Projects")!;
        var tree = new[]
            {
                new TreeWorkspace
                {
                    Root = "/project",
                    Name = "project",
                    Sessions = new[]
                    {
                        new TreeSession { Path = "/unread", Title = "unread", Unread = true },
                        new TreeSession { Path = "/pin", Title = "pinned", Pinned = true },
                        new TreeSession { Path = "/archived", Title = "archived", Archived = true },
                    },
                },
            };
        reads.Exchange = request => Task.FromResult<System.Net.Http.HttpResponseMessage?>(request.RequestUri!.AbsolutePath == "/tree" ? JsonReply(System.Text.Json.JsonSerializer.SerializeToNode(tree)!) : null);
        rail.Show(tree, "/pin");
        Dispatcher.UIThread.RunJobs();
        var selected = rail.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("session-title") && t.Text == "pinned");
        Assert.Equal(FontWeight.SemiBold, selected.FontWeight);
        var selectedOpen = rail.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "action:session.open" && AutomationProperties.GetName(b) == "pinned");
        w.MouseMove(selectedOpen.TranslatePoint(new Point(80, 15), w)!.Value);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Thickness(14, 0, 32, 0), selectedOpen.Padding);
        w.MouseMove(new Point(500, 200));
        var scope = Assert.IsType<MenuFlyout>(rail.FindControl<Button>("Scope")!.Flyout);
        scope.Items.OfType<MenuItem>().Single(i => AutomationProperties.GetAutomationId(i) == "action:session.scope.unread").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var open = rail.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "action:session.open");
        Assert.Equal("unread", AutomationProperties.GetName(open));
        string? marked = null;
        rail.MarkedViewed += path => marked = path;
        Assert.IsType<MenuFlyout>(open.ContextFlyout).Items.OfType<MenuItem>().Single(item => AutomationProperties.GetAutomationId(item) == "action:session.mark-read").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal("/unread", marked);
        scope.Items.OfType<MenuItem>().Single(i => AutomationProperties.GetAutomationId(i) == "action:session.scope.archived").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("archived", AutomationProperties.GetName(rail.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "action:session.open")));
        w.FindControl<MenuItem>("SidebarAccount")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.FindControl<Control>("SettingsPanel")!.IsVisible);
        Assert.Equal(Reasonix.Studio.Pages.I18n.MessageCatalog.Get("settings.account_and_about", Reasonix.Studio.Pages.I18n.InterfaceLanguage.Zh), w.FindControl<TextBlock>("SettingsHeading")!.Text);
    });

    /// <summary>Captures local and remote sidebars at both theme and viewport combinations.</summary>
    /// <param name="width">Client width.</param>
    /// <param name="height">Client height.</param>
    /// <param name="dark">Requested palette.</param>
    /// <returns>The native capture.</returns>
    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1440, 900, true)]
    [InlineData(760, 480, false)]
    [InlineData(760, 480, true)]
    public Task SidebarCapturesBothThemes(int width, int height, bool dark) => WithSettings("conversation", (w, reads) =>
    {
        reads.Exchange = request =>
        {
            if (request.RequestUri!.AbsolutePath != "/remotes/gpu-box/tree")
            {
                return Task.FromResult<System.Net.Http.HttpResponseMessage?>(null);
            }

            var tree = reads.Read<System.Text.Json.Nodes.JsonArray>("/remotes/gpu-box/tree");
            tree[0]!["sessions"]![0]!["lastActiveAt"] = DateTimeOffset.Now.AddHours(-2).ToUnixTimeMilliseconds();
            tree[0]!["sessions"]![1]!["lastActiveAt"] = DateTimeOffset.Now.AddDays(-7).ToUnixTimeMilliseconds();
            return Task.FromResult<System.Net.Http.HttpResponseMessage?>(JsonReply(tree));
        };
        w.Width = width;
        w.Height = height;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        w.FindControl<ProjectRail>("Projects")!.Show(reads.Read<TreeWorkspace[]>("/tree"), null);
        w.FindControl<TextBox>("RailSearch")!.Text = " ";
        Dispatcher.UIThread.RunJobs();
        w.FindControl<TextBox>("RailSearch")!.Text = string.Empty;
        Dispatcher.UIThread.RunJobs();
        var rail = w.FindControl<Control>("Rail")!;
        if (!rail.IsEffectivelyVisible)
        {
            w.FindControl<Button>("ExpandRail")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(rail.IsEffectivelyVisible);
        var output = Environment.GetEnvironmentVariable("STUDIO_PARITY_OUTPUT");
        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(output);
            Save(string.Empty);
            var scroll = w.FindControl<ProjectRail>("Projects")!.FindControl<ScrollViewer>("TreeScroll")!;
            scroll.Offset = new Vector(0, double.MaxValue);
            Dispatcher.UIThread.RunJobs();
            Save("-remote");

            void Save(string state)
            {
                using var image = new RenderTargetBitmap(new PixelSize(264, height), new Vector(96, 96));
                image.Render(rail);
                image.Save(Path.Combine(output, $"sidebar{state}-{width}x{height}.{(dark ? "dark" : "light")}.png"), PngBitmapEncoderOptions.Default);
            }
        }
    });
}
