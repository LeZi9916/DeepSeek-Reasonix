using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Shell.Host;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>The desktop-owned editor records, applies and clears a shortcut through the normal settings entry.</summary>
    /// <param name="width">The client width.</param>
    /// <param name="height">The client height.</param>
    /// <param name="dark">Whether to render the dark theme.</param>
    /// <returns>The editor interactions and captures.</returns>
    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1440, 900, true)]
    [InlineData(760, 480, false)]
    [InlineData(760, 480, true)]
    public Task GlobalShortcutEditorUsesTheDesktopRegistration(int width, int height, bool dark) => WithSettings("conversation", (w, _) =>
    {
        var saved = string.Empty;
        var releases = 0;
        using var shortcut = new GlobalShortcut(
            saved,
            (chord, _) => chord.Key == PhysicalKey.F9 ? null : () => releases++,
            binding => saved = binding,
            () => { });
        w.WindowShortcut = shortcut;
        w.Width = width;
        w.Height = height;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Navigate(w, "settings-controls");
        var input = ControlAt<TextBox>(w, "action:window.global-shortcut.binding");
        var scope = w.GetVisualDescendants().OfType<Control>().Single(control => Equals(control.Tag, "settings.global_show_hide_shortcut"));
        var scroll = w.FindControl<ScrollViewer>("SettingsScroller")!;
        scroll.Offset = new Vector(0, scope.TranslatePoint(default, w)!.Value.Y + scroll.Offset.Y - (width == 760 ? 215 : 151));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(string.Empty, input.Text);
        Capture("global-shortcut");

        ClickAt(w, "action:window.global-shortcut.record");
        Assert.True(input.IsReadOnly);
        Press(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape);
        Assert.False(input.IsReadOnly);
        var settings = w.FindControl<Grid>("SettingsPanel")!;
        Assert.True(settings.IsVisible);
        ClickAt(w, "action:window.global-shortcut.record");
        var primary = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
        Press(Key.K, primary, PhysicalKey.K);
        Assert.False(input.IsReadOnly);
        Assert.EndsWith("+K", input.Text);
        ClickAt(w, "action:window.global-shortcut.record");
        input.Text = string.Empty;
        Assert.True(input.IsReadOnly);
        Capture("global-record");
        Press(Key.Y, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.Y);
        Assert.Equal("Control+Shift+Y", input.Text);
        Assert.False(input.IsReadOnly);
        Assert.Empty(saved);
        settings.Focus();
        Capture("global-draft");

        input.Text = "CommandOrControl+Shift+Space";
        ClickAt(w, "action:window.global-shortcut.apply");
        Assert.Equal("CommandOrControl+Shift+Space", saved);
        Assert.Equal(GlobalShortcutStatus.Registered, shortcut.Status);
        settings.Focus();
        Capture("global-registered");
        input.Text = "Control+F9";
        ClickAt(w, "action:window.global-shortcut.apply");
        Assert.Equal(0, releases);
        Assert.Equal("CommandOrControl+Shift+Space", shortcut.Binding);
        Assert.Equal("Control+F9", input.Text);
        Assert.Contains(scope.GetVisualDescendants().OfType<TextBlock>(), label => label.IsEffectivelyVisible && label.Text?.StartsWith("系统未能注册", StringComparison.Ordinal) == true);

        ClickAt(w, "action:window.global-shortcut.disable");
        Assert.Equal(1, releases);
        Assert.Empty(saved);
        Assert.Equal(GlobalShortcutStatus.Disabled, shortcut.Status);
        Assert.Equal(string.Empty, input.Text);

        void Capture(string state) => CaptureFlow(w, $"native-ops-{state}-{width}x{height}", resetScroll: false);

        void Press(Key key, RawInputModifiers modifiers, PhysicalKey position)
        {
            w.KeyPress(key, modifiers, position, string.Empty);
            w.KeyRelease(key, modifiers, position, string.Empty);
            Dispatcher.UIThread.RunJobs();
        }
    });
}
