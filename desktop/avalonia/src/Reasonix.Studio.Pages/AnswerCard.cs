using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.State.Transcript;
using System;
using System.Linq;
using System.Text;

namespace Reasonix.Studio.Pages;

/// <summary>The assistant's identity, reasoning disclosure, selectable answer and message actions.</summary>
internal sealed class AnswerCard : StackPanel
{
    /// <summary>Header for the collapsible reasoning stream.</summary>
    private readonly Button _thought;

    /// <summary>Reasoning text when expanded.</summary>
    private readonly SelectableTextBlock _reasoning;

    /// <summary>Selectable Markdown message.</summary>
    private readonly SelectableTextBlock _answer;

    /// <summary>The source used for copying and quoting.</summary>
    private string _source = string.Empty;

    /// <summary>An explicit reader choice takes precedence over the live thinking state.</summary>
    private bool? _openThought;

    /// <summary>The character count displayed in the reasoning disclosure.</summary>
    private int _thoughtLength;

    /// <summary>Initializes a new instance of the <see cref="AnswerCard"/> class.</summary>
    /// <param name="item">Current answer.</param>
    /// <param name="language">Reader's language.</param>
    /// <param name="quote">Adds the chosen passage to the draft.</param>
    internal AnswerCard(SayItem item, InterfaceLanguage language, Action<string> quote)
    {
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Height = 25 };
        heading.Children.Add(new TextBlock { Text = "◉", FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
        heading.Children.Add(new TextBlock { Text = "reasonix", FontWeight = FontWeight.SemiBold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        Children.Add(heading);
        _thought = ActionButton("›", MessageCatalog.Get(MessageKey.Chat.Thinking, language));
        _thought.Margin = new Thickness(0, 8, 0, 0);
        Children.Add(_thought);
        _reasoning = new SelectableTextBlock { IsVisible = false, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 };
        Children.Add(_reasoning);
        _thought.Click += (_, _) =>
        {
            _openThought = !_reasoning.IsVisible;
            _reasoning.IsVisible = _openThought.Value;
        };
        _answer = new SelectableTextBlock
        {
            FontSize = 14,
            FontWeight = FontWeight.Medium,
            LineHeight = 24,
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetAutomationId(_answer, "class:txt");
        Children.Add(_answer);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
        var copy = ActionButton("⧉", MessageCatalog.Get(MessageKey.Common.Copy, language));
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(_source).ConfigureAwait(true);
            }
        };
        actions.Children.Add(copy);
        var cite = ActionButton("❞", MessageCatalog.Get(MessageKey.Composer.SideQuoteLabel, language));
        cite.Click += (_, _) => quote(string.IsNullOrEmpty(_answer.SelectedText) ? _source : _answer.SelectedText);
        actions.Children.Add(cite);
        Children.Add(actions);
        Update(item, language);
    }

    /// <summary>Opens the reasoning disclosure when it contains a search hit.</summary>
    /// <param name="query">The search text.</param>
    internal void RevealReasoning(string query)
    {
        if (_reasoning.Text?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true)
        {
            _openThought = true;
            _reasoning.IsVisible = true;
        }
    }

    /// <summary>Refreshes only the text that changed in this answer.</summary>
    /// <param name="item">Updated answer from the reducer.</param>
    /// <param name="language">Window language.</param>
    internal void Update(SayItem item, InterfaceLanguage language)
    {
        var reasoning = item.Reasoning ?? string.Empty;
        if (_reasoning.Text != reasoning)
        {
            _reasoning.Text = reasoning;
            _thoughtLength = reasoning.EnumerateRunes().Count();
        }

        var live = !item.Done && item.ThoughtMs is null && item.Cut is null;
        _thought.IsVisible = reasoning.Length > 0;
        _reasoning.IsVisible = _thought.IsVisible && (_openThought ?? (live && item.Text.Length == 0));
        var chars = MessageCatalog.Get("chat.n_chars", language, ("n", Format.Count(_thoughtLength, language)));
        var secs = Format.Decimals((item.ThoughtMs ?? 0) / 1000d, 1, language);
        var label = item.Cut switch
        {
            ThoughtCut.Stopped => MessageCatalog.Get("chat.thought_stopped_s", language, ("secs", secs)),
            ThoughtCut.Failed => MessageCatalog.Get("chat.thought_failed_s", language, ("secs", secs)),
            _ when live => MessageCatalog.Get(MessageKey.Chat.Thinking, language),
            _ when item.ThoughtMs > 0 => MessageCatalog.Get("chat.thought_chars_secs_s", language, ("chars", chars), ("secs", secs)),
            _ => MessageCatalog.Get("chat.thought_chars", language, ("chars", chars)),
        };
        _thought.Content = label;
        AutomationProperties.SetName(_thought, label);
        if (_source != item.Text)
        {
            _source = item.Text;
            MarkdownAnswer.Show(_answer, item.Text);
        }

        _answer.Opacity = item.Done ? 1 : 0.9;
    }

    /// <summary>Creates a quiet action with a keyboard and screen reader label.</summary>
    /// <param name="symbol">Visual mark.</param>
    /// <param name="label">Accessible action.</param>
    /// <returns>The button.</returns>
    private static Button ActionButton(string symbol, string label)
    {
        var button = new Button
        {
            Content = symbol,
            Padding = new Thickness(3, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(button, label);
        ToolTip.SetTip(button, label);
        return button;
    }
}
