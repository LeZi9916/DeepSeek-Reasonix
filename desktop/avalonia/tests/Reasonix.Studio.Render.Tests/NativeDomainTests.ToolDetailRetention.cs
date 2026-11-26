using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Wire;
using Reasonix.Studio.Pages;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Retains an open tool detail while the owning runtime publishes revised output.</summary>
    /// <returns>The workbench and live transcript interaction.</returns>
    [Fact]
    public Task WorkbenchToolDetailSurvivesLiveOutputUpdates() => WithSettings("workbench", (window, _) =>
    {
        var page = window.FindControl<SessionPage>("Conversation")!;
        var user = new UserItem { Id = "question", Text = "Read the file" };
        var tool = new ToolItem { Id = "tool", Running = true, Call = new Tool { ID = "read", Name = "read_file", Output = "partial" } };
        var running = new SessionState { Running = true, Items = [user, tool] };
        window.ShowState(running);
        Settle();
        var header = Header();
        header.FindControl<Button>("Toggle")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle();
        var detail = header.FindControl<ScrollViewer>("Output")!;
        Assert.True(detail.IsEffectivelyVisible);

        var revised = tool with
        {
            Call = tool.Call with
            {
                Output = "partial\nand more",
            },
        };
        window.ShowState(running with
        {
            Items = [user, revised],
        });
        Settle();
        Assert.Same(header, Header());
        Assert.True(detail.IsEffectivelyVisible);
        Assert.Equal("partial\nand more", header.FindControl<SelectableTextBlock>("Result")!.Text);

        var sibling = new ToolItem { Id = "sibling", Running = true, Call = new Tool { ID = "next", Name = "grep" } };
        window.ShowState(running with
        {
            Items = [user, revised, sibling],
        });
        Settle();
        Assert.Same(header, Header());
        Assert.True(detail.IsEffectivelyVisible);

        header.FindControl<Button>("Toggle")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle();
        Assert.False(detail.IsEffectivelyVisible);

        UserControl Header() => page.GetVisualDescendants().OfType<UserControl>()
            .First(control => control.GetType().Name == "ToolCallHeader" && control.IsEffectivelyVisible);

        void Settle()
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    });
}
