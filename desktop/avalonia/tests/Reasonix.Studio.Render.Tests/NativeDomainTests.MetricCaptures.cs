using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Shell.Metrics;
using Reasonix.Studio.State;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Captures native metric details in both themes and viewport sizes.</summary>
    /// <param name="width">Window width.</param><param name="height">Window height.</param><param name="dark">Requested theme.</param>
    /// <returns>The mounted metric surfaces.</returns>
    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1440, 900, true)]
    [InlineData(760, 480, false)]
    [InlineData(760, 480, true)]
    public Task MetricCardsFitBothViewportsAndThemes(int width, int height, bool dark) => WithSettings("conversation", (w, reads) =>
    {
        w.Width = width;
        w.Height = height;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        w.AdoptComposer(reads.Read<SessionStatus>("/status"));
        w.ShowState(Reasonix.Studio.Testing.ReferenceConversation.Create());
        Dispatcher.UIThread.RunJobs();
        foreach (var surface in new[] { "Speed", "Cache", "Context" })
        {
            var hover = w.FindControl<StudioHoverCard>(surface + "Hover")!;
            hover.Open();
            Dispatcher.UIThread.RunJobs();
            Assert.True(hover.IsOpen);
            var frame = hover.FindControl<Border>("Frame")!;
            Assert.InRange(frame.Bounds.Width, 120, width - 32);
            Assert.InRange(frame.Bounds.Height, 20, height - 32);
            if (surface == "Context")
            {
                var parts = w.FindControl<ContextSummaryCard>("ContextDetails")!.FindControl<StackPanel>("Parts")!;
                Assert.Equal(7, parts.Children.Count);
                Assert.All(parts.Children, row => Assert.InRange(row.Bounds.Width, 250, 350));
                Assert.All(parts.Children.OfType<Grid>(), row => Assert.NotNull(row.Children.OfType<TextBlock>().First().Foreground));
            }

            Save(frame, surface.ToLowerInvariant());
            Save(w, surface.ToLowerInvariant() + "-window");
            hover.Close();
        }

        var ledger = System.Text.Json.JsonSerializer.Deserialize<SessionStatus>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ledger-inputs.json")), System.Text.Json.JsonSerializerOptions.Web)!;
        w.AdoptComposer(reads.Read<SessionStatus>("/status") with
        {
            SessionUsage = ledger.SessionUsage,
            SessionCostQuote = ledger.SessionCostQuote,
        });
        var overview = w.FindControl<StudioHoverCard>("CacheHover")!;
        overview.Open();
        Dispatcher.UIThread.RunJobs();
        var overviewCard = w.FindControl<SessionOverviewCard>("CacheDetails")!;
        Assert.Equal("12", overviewCard.FindControl<TextBlock>("Requests")!.Text);
        Assert.Equal("128k", overviewCard.FindControl<TextBlock>("Tokens")!.Text);
        Assert.Contains("6 分 0 秒", overviewCard.FindControl<TextBlock>("WorkTime")!.Text);
        Assert.Equal(Avalonia.Media.Color.Parse(dark ? "#B4B7B0" : "#5E625C"), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(overviewCard.FindControl<TextBlock>("Title")!.Foreground).Color);
        Assert.Contains("≥", overviewCard.FindControl<TextBlock>("Cost")!.Text);
        Save(overview.FindControl<Border>("Frame")!, "cache-full");
        Save(w, "cache-full-window");
        overview.Close();

        var spark = w.FindControl<StudioSparkline>("SpeedSpark")!;
        spark.Points = new double[] { 0, 8, 14, 12, 21, 18, 25, 19, 15, 24, 28, 26 };
        spark.Peak = 28;
        spark.IsLive = true;
        spark.IsVisible = true;
        w.FindControl<TextBlock>("SpeedText")!.Text = "26.0";
        w.FindControl<SpeedDetailCard>("SpeedDetails")!.Show(new GenerationReading(26, 23.5, 0.82, 820, 34.9, spark.Points, 28), true, Reasonix.Studio.Pages.I18n.InterfaceLanguage.Zh);
        var speed = w.FindControl<StudioHoverCard>("SpeedHover")!;
        speed.Open();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(44, spark.Bounds.Width);
        Assert.Equal(13, spark.Bounds.Height);
        Save(spark, "spark");
        Save(w, "running-curve");
        speed.Close();

        void Save(Control control, string surface)
        {
            var output = Environment.GetEnvironmentVariable("STUDIO_PARITY_OUTPUT");
            if (string.IsNullOrEmpty(output))
            {
                return;
            }

            Directory.CreateDirectory(output);
            using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(control.Bounds.Size, 1), new Vector(96, 96));
            bitmap.Render(control);
            var stem = Path.Combine(output, $"metrics-{surface}-{width}x{height}.{(dark ? "dark" : "light")}");
            bitmap.Save(stem + ".png", PngBitmapEncoderOptions.Default);
            var origin = control.TranslatePoint(default, w) ?? default;
            File.WriteAllText(stem + ".geometry.json", System.Text.Json.JsonSerializer.Serialize(new
            {
                x = origin.X,
                y = origin.Y,
                width = control.Bounds.Width,
                height = control.Bounds.Height,
            }));
        }
    });
}
