using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.State;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks inner text and icon boxes rather than only their enclosing buttons.</summary>
    /// <param name="width">Viewport width.</param>
    /// <param name="dark">Palette.</param>
    /// <returns>The mounted element comparison.</returns>
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(760, false)]
    [InlineData(760, true)]
    public Task ComposerElementsMatchReference(int width, bool dark) => WithSettings("conversation", (w, reads) =>
    {
        w.Width = width;
        w.Height = width == 760 ? 480 : 900;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        w.AdoptComposer(reads.Read<SessionStatus>("/status"));
        w.FindControl<TextBox>("Composer")!.Text = "对齐聊天区的字体、颜色与消息间距。";
        Dispatcher.UIThread.RunJobs();
        var fixtures = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "composer-elements.json")))!.AsArray();
        var expected = fixtures.Single(item => (int?)item!["width"] == width && (string?)item["theme"] == (dark ? "dark" : "light"))!["elements"]!.AsObject();
        var surface = w.FindControl<Control>("ComposeSurface")!;
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(w.ClientSize, 1), new Vector(96, 96));
        bitmap.Render(w);
        var origin = surface.TranslatePoint(default, w)!.Value;
        var differences = new List<string>();
        foreach (var (name, reference) in expected)
        {
            var control = w.FindControl<Control>(name);
            if (control is null)
            {
                differences.Add(name + " missing");
                continue;
            }

            var point = control.TranslatePoint(default, surface)!.Value;
            var box = new[] { point.X, point.Y, control.Bounds.Width, control.Bounds.Height };
            for (var i = 0; i < box.Length; i++)
            {
                var target = (double)reference!["box"]![i]!;
                if (Math.Abs(box[i] - target) > 0.5)
                {
                    differences.Add($"{name} box[{i}] {target:0.###} -> {box[i]:0.###}");
                }
            }

            if (control is TextBlock text)
            {
                if (name != "ModelLabel" && text.ClipToBounds)
                {
                    differences.Add(name + " clips glyphs outside its CSS line box");
                }

                if (Math.Abs(text.FontSize - (double)reference!["fontSize"]!) > 0.1)
                {
                    differences.Add($"{name} font-size {reference["fontSize"]} -> {text.FontSize}");
                }

                var height = (double)reference!["lineHeight"]!;
                if (double.IsNaN(text.LineHeight) || Math.Abs(text.LineHeight - height) > 0.1)
                {
                    differences.Add($"{name} line-height {height} -> {text.LineHeight}");
                }

                if ((int)text.FontWeight != (int)reference["fontWeight"]!)
                {
                    differences.Add($"{name} weight {reference["fontWeight"]} -> {text.FontWeight}");
                }

                var ink = Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color;
                if (ink != Color.Parse((string)reference["color"]!))
                {
                    differences.Add($"{name} color {reference["color"]} -> {ink}");
                }

                if (reference["inkRows"] is JsonArray inkRows)
                {
                    var rectangle = new PixelRect((int)Math.Floor(origin.X + point.X), (int)Math.Floor(origin.Y + point.Y - 4), (int)Math.Ceiling(control.Bounds.Width), (int)Math.Ceiling(control.Bounds.Height + 9));
                    var pixels = new byte[rectangle.Width * rectangle.Height * 4];
                    var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                    try
                    {
                        bitmap.CopyPixels(rectangle, pinned.AddrOfPinnedObject(), pixels.Length, rectangle.Width * 4);
                    }
                    finally
                    {
                        pinned.Free();
                    }

                    var background = Color.Parse(dark ? "#181918" : "#FAFAF8");
                    var rows = Enumerable.Range(0, rectangle.Height).Where(row => Enumerable.Range(0, rectangle.Width).Any(column =>
                    {
                        var pixel = (row * rectangle.Width * 4) + (column * 4);
                        var toInk = Math.Pow(pixels[pixel] - ink.B, 2) + Math.Pow(pixels[pixel + 1] - ink.G, 2) + Math.Pow(pixels[pixel + 2] - ink.R, 2);
                        var toBackground = Math.Pow(pixels[pixel] - background.B, 2) + Math.Pow(pixels[pixel + 1] - background.G, 2) + Math.Pow(pixels[pixel + 2] - background.R, 2);
                        return toInk < toBackground;
                    })).Select(row => row + rectangle.Y - origin.Y).ToArray();
                    if (rows.Length == 0 || Math.Abs(rows.Min() - (double)inkRows[0]!) > 1 || Math.Abs(rows.Max() - (double)inkRows[1]!) > 1)
                    {
                        differences.Add($"{name} glyph rows {inkRows} -> {string.Join(",", rows.Length == 0 ? [] : new[] { rows.Min(), rows.Max() })}");
                    }
                }
            }
        }

        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
    });

    /// <summary>Context readings retain their warning, detail and amount-mask interactions.</summary>
    /// <param name="dark">Palette.</param>
    /// <returns>The changing readings.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ComposerContextReadingsFollowLiveUsage(bool dark) => WithSettings("conversation", (w, reads) =>
    {
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        w.AdoptComposer(reads.Read<SessionStatus>("/status"));
        var percent = w.FindControl<TextBlock>("MeterText")!;
        var ring = w.FindControl<Grid>("ContextRing")!;
        var context = w.FindControl<Button>("ContextToggle")!;
        w.ShowState(new SessionState { ContextUsed = 102400, Metrics = new Metrics { Hit = 1, Miss = 3 } });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("80%", percent.Text);
        Assert.Equal("25%", w.FindControl<TextBlock>("CacheText")!.Text);
        Assert.Equal(Color.Parse(dark ? "#F2925E" : "#9C4C23"), Assert.IsAssignableFrom<ISolidColorBrush>(percent.Foreground).Color);
        Assert.Equal(11, ring.Bounds.Width);
        Assert.Equal(11, ring.Bounds.Height);
        Assert.Equal(0.75, ring.Opacity);
        w.ShowState(new SessionState { ContextUsed = 24800 });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("19%", percent.Text);
        Assert.Equal(Color.Parse(dark ? "#B4B7B0" : "#5E625C"), Assert.IsAssignableFrom<ISolidColorBrush>(percent.Foreground).Color);
        context.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.FindControl<Reasonix.Studio.Controls.StudioHoverCard>("ContextHover")!.IsOpen);
        Assert.False(w.FindControl<Control>("ContextPanel")!.IsVisible);
        w.FindControl<Button>("HideAmounts")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("•••", w.FindControl<TextBlock>("WalletText")!.Text);
        w.FindControl<Button>("HideAmounts")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("¥110.00", w.FindControl<TextBlock>("WalletText")!.Text);
    });
}
