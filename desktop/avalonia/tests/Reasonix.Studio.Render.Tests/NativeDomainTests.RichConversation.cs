using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CSharpMath.Avalonia;
using Reasonix.Studio.Contracts.Wire;
using Reasonix.Studio.Pages;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Renders a Markdown pipe table, a formula, and an enabled local link in an answer.</summary>
    /// <returns>The mounted native reading surface.</returns>
    [Fact]
    public Task RichConversationRendersNativeMarkdownBlocks() => WithSettings("conversation", (window, _) =>
    {
        var page = window.FindControl<SessionPage>("Conversation")!;
        string? opened = null;
        page.WorkspaceAssets = new MarkdownAssets(
            path => path == "README.md" ? path : null,
            path =>
            {
                opened = path;
                return Task.CompletedTask;
            },
            (_, _) => Task.FromResult<Avalonia.Media.Imaging.Bitmap?>(null));
        window.ShowState(new SessionState
        {
            Items = [new SayItem { Id = "rich", Done = true, Text = "[Readme](README.md) and $x^2$.\n\n| Name | Count |\n| --- | --- |\n| Alpha | 2 |" }],
        });
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(page.GetVisualDescendants().OfType<MathView>(), math => math.LaTeX == "x^2" && math.Bounds.Width > 0);
        Assert.Contains(page.GetVisualDescendants().OfType<SelectableTextBlock>(), cell => (cell.Text ?? cell.Inlines?.Text) == "Alpha");
        var link = page.GetVisualDescendants().OfType<HyperlinkButton>().Single(button => Equals(button.Content, "Readme"));
        link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("README.md", opened);
    });

    /// <summary>Submits required extension values once and keeps their acknowledgement through re-projection.</summary>
    /// <returns>The mounted extension form.</returns>
    [Fact]
    public Task RichConversationSubmitsExtensionFormThroughTheOwner() => WithSettings("conversation", (window, _) =>
    {
        var page = window.FindControl<SessionPage>("Conversation")!;
        JsonObject? submitted = null;
        page.SubmitExtensionForm = (_, values) =>
        {
            submitted = values;
            return Task.FromResult(true);
        };
        var surface = new ExtensionSurface
        {
            PluginID = "example",
            SurfaceID = "form",
            Form = new ExtensionForm { Title = "Extension question", Fields = [new ExtensionFormField { Key = "answer", Label = "Answer", Required = true }] },
        };
        window.ShowState(new SessionState { Items = [new ExtensionItem { Id = "ext", Surface = surface }] });
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var submit = page.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "action:extensions.submit");
        Assert.False(submit.IsEnabled);
        var answer = page.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "Answer");
        answer.Text = "ready";
        Dispatcher.UIThread.RunJobs();
        Assert.True(submit.IsEnabled);
        submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("ready", (string?)submitted?["answer"]);
        Assert.False(submit.IsEffectivelyVisible);
        Assert.False(answer.IsEffectivelyEnabled);
    });
}
