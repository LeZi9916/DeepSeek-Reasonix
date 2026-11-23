using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Matches the visible reference sections, compact navigation and the start-mode label.</summary>
    /// <param name="width">Viewport width.</param>
    /// <param name="height">Viewport height.</param>
    /// <param name="dark">Whether to use the dark theme.</param>
    /// <returns>The settings navigation interaction and its captures.</returns>
    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1440, 900, true)]
    [InlineData(760, 480, false)]
    [InlineData(760, 480, true)]
    public Task SettingsNavigationUsesOnlyTheVisibleReferenceSections(int width, int height, bool dark) => WithSettings("settings-common", (w, _) =>
    {
        w.Width = width;
        w.Height = height;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();
        var placeholder = w.FindControl<TextBox>("SettingsSearch")!.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "PART_Placeholder");
        Assert.Equal(1, placeholder.Opacity);
        var navigation = w.FindControl<ListBox>("SettingsNav")!;
        Assert.Equal(
            new[] { "常用", "模型", "Agent 行为", "权限与安全", "扩展", "对话显示", "操作与性能", "外观", "数据", "连接", "账号与关于" },
            navigation.Items.Select(item => item?.ToString()));
        var content = w.FindControl<ContentControl>("SettingsContent")!;
        var startMode = content.GetVisualDescendants().OfType<Border>().Single(row => Equals(row.Tag, "safety.how_new_conversations_start"));
        Assert.Contains(startMode.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "新开的会话生效");
        CaptureFlow(w, $"navigation-common-{width}x{height}");

        var hover = navigation.ContainerFromIndex(1)!;
        w.MouseMove(hover.TranslatePoint(new Point(16, 15), w)!.Value);
        Dispatcher.UIThread.RunJobs();
        CaptureFlow(w, $"navigation-hover-{width}x{height}");

        var viewport = Assert.Single(navigation.GetVisualDescendants().OfType<ScrollViewer>());
        if (width == 760)
        {
            Assert.Equal(ScrollBarVisibility.Hidden, viewport.HorizontalScrollBarVisibility);
            var point = navigation.TranslatePoint(new Point(110, 16), w)!.Value;
            w.MouseWheel(point, new Vector(0, -30));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(viewport.Extent.Width - viewport.Viewport.Width, viewport.Offset.X, precision: 4);
        }

        var account = navigation.ContainerFromIndex(navigation.Items.Count - 1)!;
        var accountPoint = account.TranslatePoint(new Point(16, 15), w)!.Value;
        Assert.InRange(accountPoint.X, 0, navigation.Bounds.Width);
        w.MouseMove(accountPoint);
        w.MouseDown(accountPoint, MouseButton.Left);
        w.MouseUp(accountPoint, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("账号与关于", w.FindControl<TextBlock>("SettingsHeading")!.Text);
        var folder = content.GetVisualDescendants().OfType<SelectableTextBlock>().Single(text => text.Name == "Folder");
        var menu = Assert.IsType<MenuFlyout>(folder.ContextFlyout);
        var reveal = Assert.Single(menu.Items.OfType<MenuItem>());
        Assert.Equal("action:feedback.logs", AutomationProperties.GetAutomationId(reveal));
        CaptureFlow(w, $"navigation-account-{width}x{height}");
        var links = w.FindControl<WrapPanel>("SettingsLinks")!.Children.OfType<Button>().ToArray();
        Assert.Equal(new[] { "追踪导出", "账号", "版本", "日志与异常退出记录", "社区与贡献者", "赞助" }, links.Select(link => link.Content));
        links[^1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var sponsor = content.GetVisualDescendants().OfType<Control>().First(control => Equals(control.Tag, "about.support_the_project"));
        var images = sponsor.GetVisualDescendants().OfType<Image>().ToArray();
        Assert.Equal(2, images.Length);
        Assert.All(images, image => Assert.False(image.IsEffectivelyVisible));
        CaptureFlow(w, $"navigation-sponsor-{width}x{height}", resetScroll: false);
        ClickAt(w, "action:sponsor.qr");
        Assert.All(images, image =>
        {
            Assert.True(image.IsEffectivelyVisible);
            Assert.NotNull(image.Source);
        });
        links[^1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        CaptureFlow(w, $"navigation-sponsor-open-{width}x{height}", resetScroll: false);
        ClickAt(w, "action:sponsor.qr");
        Assert.All(images, image => Assert.False(image.IsEffectivelyVisible));

        w.FindControl<TextBox>("SettingsSearch")!.Text = "高级";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("没有匹配的设置", w.FindControl<TextBlock>("NoSettingsHits")!.Text);
        Assert.True(w.FindControl<TextBlock>("NoSettingsHits")!.IsEffectivelyVisible);
    });

    /// <summary>Search fills the content column and dismisses before the settings window does.</summary>
    /// <param name="width">Viewport width.</param>
    /// <param name="height">Viewport height.</param>
    /// <returns>The layout and Escape interactions.</returns>
    [Theory]
    [InlineData(1440, 900)]
    [InlineData(760, 480)]
    public Task SettingsSearchUsesTheReferenceContentColumn(int width, int height) => WithSettings("settings-common", (w, _) =>
    {
        w.Width = width;
        w.Height = height;
        var search = w.FindControl<TextBox>("SettingsSearch")!;
        var results = w.FindControl<Border>("SearchResultsPanel")!;
        var hits = w.FindControl<ListBox>("SettingsHits")!;
        search.Focus();
        search.Text = "语言";
        Dispatcher.UIThread.RunJobs();
        Assert.True(results.IsEffectivelyVisible);
        Assert.True(hits.IsEffectivelyVisible);
        CaptureFlow(w, $"settings-search-{width}x{height}");

        var origin = results.TranslatePoint(default, w)!.Value;
        Assert.Equal(240, origin.X);
        Assert.Equal(48, origin.Y);
        Assert.Equal(width - 240, results.Bounds.Width);
        Assert.Equal(height - 48, results.Bounds.Height);
        var result = Assert.Single(hits.GetVisualDescendants().OfType<ListBoxItem>());
        Assert.Equal(new Point(263, 106), result.TranslatePoint(default, w));
        Assert.Equal(54, result.Bounds.Height);
        var point = result.TranslatePoint(new Point(20, 12), w)!.Value;
        w.MouseMove(point);
        w.MouseDown(point, MouseButton.Left);
        w.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.False(results.IsEffectivelyVisible);

        search.Focus();
        search.Text = "xyzzy";
        Dispatcher.UIThread.RunJobs();
        Assert.True(results.IsEffectivelyVisible);
        Assert.False(hits.IsEffectivelyVisible);
        Assert.Equal("没有匹配的设置", w.FindControl<TextBlock>("NoSettingsHits")!.Text);
        w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        w.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Assert.False(results.IsEffectivelyVisible);
        Assert.True(string.IsNullOrEmpty(search.Text));
        Assert.True(search.IsFocused);
        Assert.True(w.FindControl<Grid>("SettingsPanel")!.IsVisible);
        w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        w.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.FindControl<Grid>("SettingsPanel")!.IsVisible);
    });
}
