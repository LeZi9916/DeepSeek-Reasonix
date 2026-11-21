using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Events;
using Reasonix.Studio.Contracts.Wire;
using Reasonix.Studio.Pages;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Baseline.Tests;

/// <summary>Exercises choices, typed replies and a refused answer through the rendered question card.</summary>
[Collection(HeadlessCollection.Name)]
public sealed class QuestionFormTests
{
    /// <summary>A rejection leaves the form editable and an unrelated stream event keeps its choices.</summary>
    /// <returns>The window interaction.</returns>
    [Fact]
    public async Task ARefusedAnswerCanBeRetriedWithoutRetyping()
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(
            () =>
            {
                var calls = new List<IReadOnlyList<QuestionSelection>>();
                var page = new SessionPage
                {
                    Language = InterfaceLanguage.En,
                    EngageQuestion = _ => Task.FromResult(true),
                    SubmitAnswers = (_, answers) =>
                    {
                        calls.Add(answers);
                        return Task.FromResult(calls.Count > 1);
                    },
                };
                var window = new Window { Content = page, Width = 760, Height = 480 };
                window.Show();
                var ask = new AskItem
                {
                    Id = "row",
                    Ask = new Ask
                    {
                        ID = "ask",
                        Questions = new List<AskQuestion>
                        {
                            new() { ID = "q", Prompt = "Choose", Options = new List<AskOption> { new() { Label = "A" }, new() { Label = "B" } } },
                        },
                    },
                };
                page.Show(new SessionState { Items = new[] { ask } });
                Dispatcher.UIThread.RunJobs();
                var controls = page.GetVisualDescendants();
                var option = controls.OfType<RadioButton>().Single(o => Equals(o.Content, "B"));
                option.IsChecked = true;
                controls.OfType<TextBox>().Single().Text = "additional detail";
                Dispatcher.UIThread.RunJobs();
                var confirm = controls.OfType<Button>().Single(b => Equals(b.Content, "Confirm"));
                confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                page.Show(new SessionState { Items = new TranscriptItem[] { ask, new NoticeItem { Id = "n", Level = "info", Text = "next" } } });
                Dispatcher.UIThread.RunJobs();
                Assert.Same(option, page.GetVisualDescendants().OfType<RadioButton>().Single(o => Equals(o.Content, "B")));
                confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(2, calls.Count);
                Assert.All(calls, call => Assert.Equal(new[] { "B", "additional detail" }, Assert.Single(call).Selected));
                window.Close();
            },
            default);
    }
}
