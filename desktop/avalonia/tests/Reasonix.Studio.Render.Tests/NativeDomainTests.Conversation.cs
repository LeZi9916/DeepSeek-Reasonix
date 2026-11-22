using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System.Linq;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks the reading face, semantic ink and collapsed work before the answer actions.</summary>
    /// <param name="dark">Requested palette.</param>
    /// <returns>The mounted conversation.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ConversationUsesReferencePresentation(bool dark) => WithSettings("workbench", (w, _) =>
    {
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        w.ShowState(Reasonix.Studio.Testing.ReferenceConversation.Create());
        Dispatcher.UIThread.RunJobs();
        var page = w.FindControl<SessionPage>("Conversation")!;
        var texts = page.GetVisualDescendants().OfType<SelectableTextBlock>().Where(t => t.Name is "Message" or "Answer").ToArray();
        Assert.Equal(4, texts.Length);
        foreach (var text in texts)
        {
            Assert.Equal(14, text.FontSize);
            Assert.Equal(FontWeight.Medium, text.FontWeight);
            Assert.Equal(Color.Parse(dark ? "#E7E8E3" : "#292B29"), Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color);
            Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(text.FontFamily, text.FontStyle, text.FontWeight), out var face));
            Assert.Equal("Reasonix Workbench UI", face.FamilyName);
        }

        var toggle = page.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("activity-toggle"));
        var body = page.GetVisualDescendants().OfType<StackPanel>().First(p => p.Name == "ActivityBody");
        Assert.False(body.IsEffectivelyVisible);
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(body.IsEffectivelyVisible);
        Assert.Equal((1, 2), page.FindMatch("edit_file", 0));
    });

    /// <summary>Reasoning, selection and quotation survive streamed answer updates.</summary>
    /// <returns>The live answer interaction.</returns>
    [Fact]
    public Task ConversationStreamsAndQuotesSelection() => WithSettings("conversation", (w, _) =>
    {
        var item = new SayItem { Id = "answer", Text = "first answer", Reasoning = "private reasoning" };
        var page = w.FindControl<SessionPage>("Conversation")!;
        w.ShowState(new SessionState { Items = new[] { item } });
        Dispatcher.UIThread.RunJobs();
        var answer = page.GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => t.Text == item.Text);
        page.FindMatch("private", 0);
        answer.SelectionStart = 0;
        answer.SelectionEnd = 5;
        w.ShowState(new SessionState { Items = new[] { item with { Text = "first answer continues", Done = true } } });
        Assert.Same(answer, page.GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => t.Text == "first answer continues"));
        Assert.Equal("first", answer.SelectedText);
        Assert.True(page.GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => t.Text == item.Reasoning).IsEffectivelyVisible);
        page.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "Cite").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Contains("first", w.FindControl<TextBox>("Composer")!.Text);
    });

    /// <summary>Captures normal disclosure and draft interactions at the indexed viewport.</summary>
    /// <param name="window">Mounted conversation window.</param>
    /// <param name="output">Native render directory.</param>
    /// <param name="file">Indexed viewport and palette.</param>
    private static void ConversationScreenProofs(Reasonix.Studio.Shell.MainWindow window, string output, string file)
    {
        var page = window.FindControl<SessionPage>("Conversation")!;
        var thought = page.GetVisualDescendants().OfType<Button>().Last(b => b.Name == "Thought");
        var activity = page.GetVisualDescendants().OfType<Button>().Last(b => b.Classes.Contains("activity-toggle"));
        thought.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        activity.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Save("expanded");
        thought.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        activity.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var composer = window.FindControl<TextBox>("Composer")!;
        composer.Text = "对齐聊天区的字体、颜色与消息间距。";
        composer.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.FindControl<Button>("Send")!.IsEnabled);
        Save("draft");

        void Save(string state)
        {
            Dispatcher.UIThread.RunJobs();
            using var image = new Avalonia.Media.Imaging.RenderTargetBitmap(Avalonia.PixelSize.FromSize(window.ClientSize, 1), new Avalonia.Vector(96, 96));
            image.Render(window);
            image.Save(System.IO.Path.Combine(output, file.Replace("conversation", "conversation-" + state, System.StringComparison.Ordinal).Replace(".json", ".png", System.StringComparison.Ordinal)), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
    }
}
