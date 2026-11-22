using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.Shell.Workspace;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Captures real native workspace surfaces in both viewport and theme variants.</summary>
    /// <param name="width">Window width.</param><param name="height">Window height.</param>
    /// <param name="dark">Requested appearance.</param><returns>The captured native surfaces.</returns>
    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1440, 900, true)]
    [InlineData(760, 480, false)]
    [InlineData(760, 480, true)]
    public Task WorkspaceCapturesBothThemes(int width, int height, bool dark) => WithSettings("conversation", (w, reads) =>
    {
        w.Width = width;
        w.Height = height;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        w.AdoptComposer(reads.Read<SessionStatus>("/status"));
        w.FindControl<ProjectRail>("Projects")!.Show(reads.Read<TreeWorkspace[]>("/tree"), null);
        w.ShowState(Reasonix.Studio.Testing.ReferenceConversation.Create());
        w.FindControl<TextBox>("RailSearch")!.Text = " ";
        Dispatcher.UIThread.RunJobs();
        w.FindControl<TextBox>("RailSearch")!.Text = string.Empty;
        Dispatcher.UIThread.RunJobs();
        w.FindControl<TextBox>("Composer")!.Focus();
        w.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
        w.KeyRelease(Key.K, RawInputModifiers.Control, PhysicalKey.K, "k");
        Dispatcher.UIThread.RunJobs();
        var search = w.GetVisualDescendants().OfType<WorkspaceSearchPanel>().Single();
        Assert.True(search.IsVisible);
        Save(w, "palette");
        search.Hide();
        if (!w.FindControl<Control>("Rail")!.IsEffectivelyVisible)
        {
            w.FindControl<Button>("ExpandRail")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        var row = w.FindControl<ProjectRail>("Projects")!.GetVisualDescendants().OfType<Button>().First(button => AutomationProperties.GetAutomationId(button) == "action:session.open");
        var menu = Assert.IsType<MenuFlyout>(row.ContextFlyout);
        row.Focus();
        w.KeyPress(Key.F10, RawInputModifiers.Shift, PhysicalKey.F10, string.Empty);
        w.KeyRelease(Key.F10, RawInputModifiers.Shift, PhysicalKey.F10, string.Empty);
        Dispatcher.UIThread.RunJobs();
        Assert.True(menu.IsOpen);
        Assert.True(menu.Items.OfType<MenuItem>().First(item => item.IsEnabled).IsKeyboardFocusWithin);
        var popup = menu.Items.OfType<MenuItem>().First().GetVisualAncestors().OfType<MenuFlyoutPresenter>().FirstOrDefault() as Control
            ?? TopLevel.GetTopLevel(menu.Items.OfType<MenuItem>().First());
        Assert.NotNull(popup);
        Save(popup, "session-menu");
        menu.Hide();
        reads.Exchange = request => Task.FromResult<HttpResponseMessage?>(request.RequestUri!.AbsolutePath switch
        {
            "/share" => JsonReply(reads.Read<JsonObject>("/share")),
            "/tree" when request.RequestUri.Query.Contains("order=recent", StringComparison.Ordinal) => JsonReply(reads.Read<JsonArray>("/tree?order=recent")),
            _ => null,
        });
        w.FindControl<ProjectRail>("Projects")!.FindControl<Button>("RecentTab")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var recent = Assert.Single(w.GetVisualDescendants().OfType<RecentSessionsPanel>());
        Assert.NotEqual(0, recent.FindControl<ListBox>("Rows")!.ItemCount);
        Save(w, "recent");
        recent.Hide();
        using var http = new HttpClient(reads) { BaseAddress = new Uri("http://127.0.0.1:4000") };
        var card = new DeviceAccessCard { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(16, 60) };
        Assert.IsType<Grid>(w.FindControl<LayoutTransformControl>("ScaledWindow")!.Child).Children.Add(card);
        card.OpenAsync(new StudioPageClient(http), InterfaceLanguage.Zh).GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
        Save(card, "device-access");
        card.IsVisible = false;
        Navigate(w, "settings-account");
        var community = Assert.Single(w.GetVisualDescendants().OfType<CommunityControl>());
        community.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        Save(community, "community");

        void Save(Control control, string surface)
        {
            var output = Environment.GetEnvironmentVariable("STUDIO_PARITY_OUTPUT");
            if (string.IsNullOrEmpty(output))
            {
                return;
            }

            Directory.CreateDirectory(output);
            using var image = new RenderTargetBitmap(PixelSize.FromSize(control.Bounds.Size, 1), new Vector(96, 96));
            image.Render(control);
            image.Save(Path.Combine(output, $"workspace-{surface}-{width}x{height}.{(dark ? "dark" : "light")}.png"), PngBitmapEncoderOptions.Default);
        }
    });
}
