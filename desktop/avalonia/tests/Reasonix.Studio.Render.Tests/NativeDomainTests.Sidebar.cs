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

    /// <summary>Checks the footer button’s immediate Electron hover colours.</summary>
    /// <param name="motion">Whether the window allows visual effects.</param>
    /// <returns>The mounted settings button interaction.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task SidebarSettingsUsesImmediateReferenceHover(bool motion) => WithSettingsAsync(
        "conversation",
        async (window, _) =>
        {
            await WaitForSettingsFrame(() => window.Classes.Contains("motion") == motion).ConfigureAwait(true);
            var button = window.FindControl<Button>("OpenSettings")!;
            var surface = button.GetVisualDescendants().OfType<Border>().First();
            var rest = ColorOfSurface();
            Assert.True(window.TryFindResource("OverlayBrush", window.ActualThemeVariant, out var overlay));
            var highlighted = Assert.IsAssignableFrom<ISolidColorBrush>(overlay).Color;
            Assert.NotEqual(rest, highlighted);
            var icon = button.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single();
            var restingInk = Assert.IsAssignableFrom<ISolidColorBrush>(icon.Stroke).Color;
            Assert.True(window.TryFindResource("TextBrush", window.ActualThemeVariant, out var text));
            var hoverInk = Assert.IsAssignableFrom<ISolidColorBrush>(text).Color;

            var outside = new Point(500, 160);
            window.MouseMove(outside);
            window.MouseMove(button.TranslatePoint(new Point(16, 16), window)!.Value);
            Assert.Equal(highlighted, ColorOfSurface());
            Assert.Equal(hoverInk, Assert.IsAssignableFrom<ISolidColorBrush>(icon.Stroke).Color);

            window.MouseMove(outside);
            Assert.Equal(rest, ColorOfSurface());
            Assert.Equal(restingInk, Assert.IsAssignableFrom<ISolidColorBrush>(icon.Stroke).Color);

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForSettingsFrame(() => window.FindControl<Control>("SettingsPanel")!.IsVisible).ConfigureAwait(true);

            Color ColorOfSurface() => Assert.IsAssignableFrom<ISolidColorBrush>(surface.Background).Color;
        },
        allowMotion: motion);

    /// <summary>Keeps the quick links open across pointer movement within their footer and popup.</summary>
    /// <returns>The native footer hover and click interaction.</returns>
    [Fact]
    public Task SidebarQuickLinksStayOpenUntilThePointerLeavesBothSurfaces() => WithSettingsAsync(
        "conversation",
        async (window, _) =>
        {
            var settings = window.FindControl<Button>("OpenSettings")!;
            var popup = window.FindControl<Avalonia.Controls.Primitives.Popup>("QuickLinksPopup")!;
            var menu = window.FindControl<Border>("QuickLinksFrame")!;
            var opens = 0;
            var closes = 0;
            popup.Opened += (_, _) => opens++;
            popup.Closed += (_, _) => closes++;
            window.UpdateLayout();
            var trigger = settings.TranslatePoint(new Point(18, 16), window)!.Value;

            window.MouseMove(trigger);
            await WaitForSettingsFrame(() => popup.IsOpen).ConfigureAwait(true);
            window.MouseMove(trigger + new Vector(1, 0));
            await Task.Delay(400).ConfigureAwait(true);
            Assert.True(popup.IsOpen);
            Assert.True(settings.IsPointerOver);
            Assert.Equal(1, opens);
            Assert.Equal(0, closes);

            var item = window.FindControl<StackPanel>("QuickLinksRows")!.Children.OfType<Button>().First();
            await WaitForSettingsFrame(() => item.Bounds.Width > 0).ConfigureAwait(true);
            window.MouseMove(item.TranslatePoint(new Point(18, 10), window)!.Value);
            await Task.Delay(220).ConfigureAwait(true);
            Assert.True(menu.IsPointerOver);
            Assert.True(popup.IsOpen);

            window.MouseMove(new Point(620, 160));
            await Task.Delay(60).ConfigureAwait(true);
            window.MouseMove(trigger);
            await Task.Delay(220).ConfigureAwait(true);
            Assert.True(popup.IsOpen);
            Assert.Equal(1, opens);

            var composer = window.FindControl<TextBox>("Composer")!;
            var draft = composer.TranslatePoint(new Point(40, 10), window)!.Value;
            window.MouseDown(draft, Avalonia.Input.MouseButton.Left);
            window.MouseUp(draft, Avalonia.Input.MouseButton.Left);
            Assert.False(popup.IsOpen);
            Assert.True(composer.IsFocused);
            Assert.Equal(1, closes);

            window.MouseMove(trigger);
            await WaitForSettingsFrame(() => popup.IsOpen).ConfigureAwait(true);
            window.MouseDown(trigger, Avalonia.Input.MouseButton.Left);
            window.MouseUp(trigger, Avalonia.Input.MouseButton.Left);
            await WaitForSettingsFrame(() => window.FindControl<Control>("SettingsPanel")!.IsVisible).ConfigureAwait(true);
            Assert.False(popup.IsOpen);
            Assert.Equal(2, closes);
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
        w.FindControl<Button>("OpenAccount")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var shortcuts = w.FindControl<Avalonia.Controls.Primitives.Popup>("QuickLinksPopup")!;
        Assert.True(shortcuts.IsOpen);
        var destinations = w.FindControl<StackPanel>("QuickLinksRows")!.Children.OfType<Button>().ToArray();
        Assert.Equal(new[] { "usage", "files", "tools", "ext", "memory", "remote" }, destinations.Select(button => Assert.IsType<string>(button.Tag)));
        destinations.Single(button => Equals(button.Tag, "usage")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(shortcuts.IsOpen);
        Assert.True(w.FindControl<Control>("SettingsPanel")!.IsVisible);
        Assert.Equal(Reasonix.Studio.Pages.I18n.MessageCatalog.Get("app.usage", Reasonix.Studio.Pages.I18n.InterfaceLanguage.Zh), w.FindControl<TextBlock>("SettingsHeading")!.Text);
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
