using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.Shell.Workspace;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks the workspace move dialog's reference card entrance.</summary>
    /// <param name="dark">Whether the conversation uses the dark theme.</param>
    /// <returns>The move-dialog entrance check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task WorkspaceMoveDialogEntersWithTheReferenceCardMotion(bool dark) =>
        WithSettingsAsync(
            "conversation",
            async (window, _) =>
            {
                window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                await WaitForSettingsFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
                var dialog = OpenDialog(window);
                var card = dialog.FindControl<Border>("DialogCard")!;
                await WaitForSettingsFrame(() => card.Opacity is > 0 and < 0.999 && card.RenderTransform!.Value.M32 > 0.4).ConfigureAwait(true);
                await WaitForSettingsFrame(() => card.Opacity == 1 && card.RenderTransform!.Value.M32 == 0).ConfigureAwait(true);
            },
            allowMotion: true);

    /// <summary>Keeps the move dialog at its final pose when motion is off.</summary>
    /// <returns>The reduced-motion move-dialog check.</returns>
    [Fact]
    public Task WorkspaceMoveDialogHonorsReducedMotion() =>
        WithSettingsAsync(
            "conversation",
            (window, _) =>
            {
                var dialog = OpenDialog(window);
                var card = dialog.FindControl<Border>("DialogCard")!;
                Assert.Equal(1d, card.Opacity);
                Assert.Equal(0d, card.RenderTransform?.Value.M32 ?? 0);
                return Task.CompletedTask;
            });

    /// <summary>Opens the real move dialog through the sidebar action.</summary>
    /// <param name="window">The open shell.</param>
    /// <returns>The dialog control.</returns>
    private static WorkspaceMoveDialog OpenDialog(Reasonix.Studio.Shell.MainWindow window)
    {
        var rail = window.FindControl<Reasonix.Studio.Pages.Rail.ProjectRail>("Projects")!;
        var row = new TreeSession { Path = "/from/session", Title = "moving conversation" };
        rail.Show(
            new[]
            {
                new TreeWorkspace { Root = "/from", Name = "from", Remembered = true, Sessions = new[] { row } },
                new TreeWorkspace { Root = "/to", Name = "to", Remembered = true },
            },
            null);
        rail.RequestAction(new Reasonix.Studio.Pages.Rail.WorkspaceAction(
            "session.move-to-start",
            new SessionOpenTarget(string.Empty, "/from", row.Path),
            row));
        window.UpdateLayout();
        return window.GetVisualDescendants().OfType<WorkspaceMoveDialog>().Single();
    }
}
