using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Workspace;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Every reference sidebar command opens its actual native destination.</summary>
    /// <param name="query">The reference command alias.</param><param name="heading">Canonical native destination heading.</param>
    /// <returns>The mounted command result.</returns>
    [Theory]
    [InlineData("storage disk", "settings.data")]
    [InlineData("tools mcp", "ext.extension")]
    [InlineData("ssh local", "usage.connections")]
    [InlineData("model provider", "common.models")]
    [InlineData("usage cost", "app.usage")]
    [InlineData("community discord", "settings.account_and_about")]
    public Task WorkspacePaletteReferenceCommandsOpenActualDestinations(string query, string heading) => WithSettings("conversation", (w, _) =>
    {
        w.FindControl<TextBox>("Composer")!.Focus();
        var primary = System.OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
        w.KeyPress(Key.K, primary, PhysicalKey.K, "k");
        w.KeyRelease(Key.K, primary, PhysicalKey.K, "k");
        Dispatcher.UIThread.RunJobs();
        var palette = Assert.Single(w.GetVisualDescendants().OfType<WorkspaceSearchPanel>());
        palette.FindControl<TextBox>("Query")!.Text = ">" + query;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(palette.FindControl<ListBox>("Results")!.Items.OfType<WorkspaceSearchHit>(), hit => hit.Selectable);
        palette.FindControl<TextBox>("Query")!.Focus();
        w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        w.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.FindControl<Control>("SettingsPanel")!.IsVisible);
        Assert.Equal(MessageCatalog.Get(heading, InterfaceLanguage.Zh), w.FindControl<TextBlock>("SettingsHeading")!.Text);
        if (query == "community discord")
        {
            Assert.Contains(w.FindControl<StackPanel>("SettingsBody")!.GetVisualDescendants().OfType<Control>(), control => Equals(control.Tag, "common.community_and_contributors"));
        }
    });
}
