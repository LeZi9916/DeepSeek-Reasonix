using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Workspace;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>The native community destination uses bundled assets and opens a concrete URL only on activation.</summary>
    /// <returns>The actual community controls and explicit host callback.</returns>
    [Fact]
    public Task WorkspaceCommunityUsesBundledAssetsAndExplicitActivation() => WithSettings("conversation", (w, _) =>
    {
        var opened = new List<string>();
        var community = new CommunityControl();
        community.Configure(
            InterfaceLanguage.En,
            url =>
            {
                opened.Add(url);
                return Task.CompletedTask;
            });
        Assert.IsType<Grid>(w.FindControl<LayoutTransformControl>("ScaledWindow")!.Child).Children.Add(community);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(opened);
        Assert.Equal(new PixelSize(296, 296), Assert.IsType<Bitmap>(community.FindControl<Image>("QqQr")!.Source).PixelSize);
        Assert.False(community.FindControl<Image>("DouyinQr")!.IsVisible);
        var toggle = community.FindControl<Button>("ToggleDouyin")!;
        Click(toggle);
        Assert.True(community.FindControl<Image>("DouyinQr")!.IsVisible);
        var profiles = community.FindControl<WrapPanel>("People")!;
        Assert.Equal(24, profiles.Children.Count);
        Click(community.FindControl<Button>("MorePeople")!);
        Assert.True(profiles.Children.Count > 24);
        var profile = profiles.Children.OfType<Button>().First();
        Click(profile);
        Assert.Equal("https://github.com/" + AutomationProperties.GetName(profile), Assert.Single(opened));

        void Click(Control control)
        {
            var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), w)!.Value;
            w.MouseMove(point);
            w.MouseDown(point, MouseButton.Left);
            w.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
    });
}
