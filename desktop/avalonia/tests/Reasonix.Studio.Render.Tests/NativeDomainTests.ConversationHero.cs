using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Offers three starter prompts and removes the cover when a message arrives.</summary>
    /// <returns>The mounted conversation and composer.</returns>
    [Fact]
    public Task EmptyConversationStartsTheDraftFromTheCover() => WithSettings("conversation", (window, _) =>
    {
        var page = window.FindControl<SessionPage>("Conversation")!;
        window.ShowState(new SessionState());
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var starters = page.GetVisualDescendants().OfType<Button>()
            .Where(button => AutomationProperties.GetAutomationId(button) == "action:hero.suggest" && button.IsEffectivelyVisible)
            .ToArray();
        Assert.Equal(3, starters.Length);
        starters[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("解释这个项目的结构", window.FindControl<TextBox>("Composer")!.Text);
        window.ShowState(new SessionState { Items = [new UserItem { Id = "q", Text = "hello" }] });
        Assert.All(starters, button => Assert.False(button.IsEffectivelyVisible));
    });
}
