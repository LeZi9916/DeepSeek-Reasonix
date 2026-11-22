using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Workspace;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Search promotes session best matches and remembers selected canonical hits.</summary>
    /// <returns>The visible search ordering.</returns>
    [Fact]
    public Task WorkspacePaletteBestMatchAndRecentFollowActualSelection() => WithSettings("conversation", (w, _) =>
    {
        var panel = new WorkspaceSearchPanel();
        Assert.IsType<Grid>(w.FindControl<LayoutTransformControl>("ScaledWindow")!.Child).Children.Add(panel);
        var entries = new[] { new WorkspaceSearchHit("actions", "shared command", "action", () => { }), new WorkspaceSearchHit("sessions", "shared session", "project", () => { }) };
        panel.Open(entries, InterfaceLanguage.En);
        panel.FindControl<TextBox>("Query")!.Text = "shared";
        Dispatcher.UIThread.RunJobs();
        var results = panel.FindControl<ListBox>("Results")!;
        Assert.Equal("shared session", results.Items.OfType<WorkspaceSearchHit>().First(hit => hit.Selectable).Label);
        panel.FindControl<TextBox>("Query")!.Focus();
        w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        w.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        Dispatcher.UIThread.RunJobs();
        panel.Open(entries, InterfaceLanguage.En);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(MessageCatalog.Get("app.recently_used", InterfaceLanguage.En), results.Items.OfType<WorkspaceSearchHit>().First().Label);
        Assert.Equal("shared session", results.Items.OfType<WorkspaceSearchHit>().First(hit => hit.Selectable).Label);
    });

    /// <summary>Prefix scope input becomes a badge and Backspace leaves the scope without editing a label.</summary>
    /// <returns>The actual input keyboard flow.</returns>
    [Fact]
    public Task WorkspacePalettePrefixAndBackspaceKeepScopeExplicit() => WithSettings("conversation", (w, _) =>
    {
        var panel = new WorkspaceSearchPanel();
        Assert.IsType<Grid>(w.FindControl<LayoutTransformControl>("ScaledWindow")!.Child).Children.Add(panel);
        panel.Open(new[] { new WorkspaceSearchHit("settings", "Appearance", "appearance", () => { }) }, InterfaceLanguage.En);
        var query = panel.FindControl<TextBox>("Query")!;
        query.Text = ",";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(string.Empty, query.Text);
        Assert.True(panel.FindControl<Border>("ScopeBadge")!.IsVisible);
        query.Focus();
        w.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, "\b");
        w.KeyRelease(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, "\b");
        Dispatcher.UIThread.RunJobs();
        Assert.False(panel.FindControl<Border>("ScopeBadge")!.IsVisible);
    });
}
