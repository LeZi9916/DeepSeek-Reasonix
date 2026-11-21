using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Reasonix.Studio.Contracts.Events;
using Reasonix.Studio.Contracts.Wire;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Pages;

/// <summary>A question form that reports choices only after explicit confirmation.</summary>
internal sealed class QuestionForm : StackPanel
{
    /// <summary>Initializes a new instance of the <see cref="QuestionForm"/> class.</summary>
    /// <param name="ask">The question batch from the kernel.</param>
    /// <param name="language">The window language.</param>
    /// <param name="engage">Keeps a timed prompt open while editing.</param>
    /// <param name="submit">Sends the completed batch.</param>
    public QuestionForm(
        Ask ask,
        InterfaceLanguage language,
        Func<string, Task<bool>> engage,
        Func<string, IReadOnlyList<QuestionSelection>, Task<bool>> submit)
    {
        Spacing = 8;
        Children.Add(new TextBlock { Text = MessageCatalog.Get(MessageKey.Chat.AskUser, language), FontWeight = FontWeight.SemiBold });
        var reads = new List<Func<QuestionSelection>>();
        var confirm = new Button { Content = MessageCatalog.Get(MessageKey.Common.Confirm, language) };
        var engaged = ask.SkipInMs <= 0;
        var sending = false;
        foreach (var question in ask.Questions)
        {
            Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(question.Header) ? question.Prompt : question.Header + Environment.NewLine + question.Prompt,
                TextWrapping = TextWrapping.Wrap,
            });
            var options = new List<ToggleButton>();
            var group = Guid.NewGuid().ToString("N");
            foreach (var option in question.Options ?? Enumerable.Empty<AskOption>())
            {
                ToggleButton choice = question.Multi ? new CheckBox() : new RadioButton { GroupName = group };
                choice.Content = option.Label;
                choice.IsChecked = question.Default?.Contains(option.Label) == true;
                ToolTip.SetTip(choice, option.Description);
                options.Add(choice);
                Children.Add(choice);
            }

            var other = new TextBox { PlaceholderText = MessageCatalog.Get(MessageKey.Ext.Other, language) };
            Children.Add(other);
            reads.Add(() =>
            {
                var selected = options.Where(o => o.IsChecked == true).Select(o => o.Content!.ToString()!).ToList();
                if (!string.IsNullOrWhiteSpace(other.Text))
                {
                    selected.Add(other.Text.Trim());
                }

                return new QuestionSelection(question.ID, selected);
            });
            foreach (var option in options)
            {
                option.IsCheckedChanged += (_, _) => Changed();
            }

            other.TextChanged += (_, _) => Changed();
        }

        Children.Add(confirm);
        Refresh();
        confirm.Click += async (_, _) =>
        {
            if (sending || !confirm.IsEnabled)
            {
                return;
            }

            sending = true;
            IsEnabled = false;
            var answers = reads.Select(read => read()).ToArray();
            if (!await submit(ask.ID, answers).ConfigureAwait(true))
            {
                sending = false;
                IsEnabled = true;
                Refresh();
            }
        };

        void Refresh()
        {
            confirm.IsEnabled = !sending && reads.Count > 0 && reads.All(read => read().Selected.Count > 0);
        }

        async void Changed()
        {
            Refresh();
            if (!engaged)
            {
                engaged = true;
                engaged = await engage(ask.ID).ConfigureAwait(true);
            }
        }
    }
}
