using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.State.Transcript;
using System;
using System.Linq;
using System.Text;

namespace Reasonix.Studio.Pages;

/// <summary>The assistant's identity, reasoning disclosure, selectable answer and message actions.</summary>
internal sealed partial class AnswerCard : UserControl
{
    /// <summary>Header for the collapsible reasoning stream.</summary>
    private readonly Button _thought;

    /// <summary>Reasoning text when expanded.</summary>
    private readonly SelectableTextBlock _reasoning;

    /// <summary>The optional first line of collapsed reasoning.</summary>
    private readonly TextBlock _preview;

    /// <summary>The defaults shown in the settings preview.</summary>
    private readonly TranscriptFolding _folding;

    /// <summary>Persists an explicit choice across stream updates and settings changes.</summary>
    private readonly Action<bool> _remember;

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
    /// <param name="folding">The transcript defaults.</param><param name="thoughtChoice">A retained reader choice.</param>
    /// <param name="remember">Records a manual choice for this answer.</param>
    internal AnswerCard(SayItem item, InterfaceLanguage language, Action<string> quote, TranscriptFolding folding, bool? thoughtChoice, Action<bool> remember)
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
        _thought = this.FindControl<Button>("Thought")!;
        _reasoning = this.FindControl<SelectableTextBlock>("Reasoning")!;
        _preview = this.FindControl<TextBlock>("ReasoningPreview")!;
        _answer = this.FindControl<SelectableTextBlock>("Answer")!;
        _folding = folding;
        _openThought = thoughtChoice;
        _remember = remember;
        _thought.Click += (_, _) =>
        {
            _openThought = !_reasoning.IsVisible;
            _remember(_openThought.Value);
            ShowReasoning(_openThought.Value);
        };
        var copy = this.FindControl<Button>("Copy")!;
        Label(copy, MessageCatalog.Get(MessageKey.Common.Copy, language));
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(_source).ConfigureAwait(true);
            }
        };
        var cite = this.FindControl<Button>("Cite")!;
        Label(cite, MessageCatalog.Get(MessageKey.Composer.SideQuoteLabel, language));
        cite.Click += (_, _) => quote(SelectedPassage());
        Label(this.FindControl<Button>("More")!, MessageCatalog.Get(MessageKey.Common.More, language));
        var copyMore = this.FindControl<MenuItem>("CopyMore")!;
        copyMore.Header = MessageCatalog.Get(MessageKey.Common.Copy, language);
        copyMore.Click += (_, _) => copy.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        var quoteMore = this.FindControl<MenuItem>("QuoteMore")!;
        quoteMore.Header = MessageCatalog.Get(MessageKey.Composer.SideQuoteLabel, language);
        quoteMore.Click += (_, _) => quote(SelectedPassage());
        Update(item, language);
    }

    /// <summary>Opens the reasoning disclosure when it contains a search hit.</summary>
    /// <param name="query">The search text.</param>
    internal void RevealReasoning(string query)
    {
        if (_reasoning.Text?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true)
        {
            _openThought = true;
            _remember(true);
            ShowReasoning(true);
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

        if (_folding.ThinkingPreview && item.Cut is null)
        {
            var firstLine = reasoning.AsSpan().TrimStart();
            var newline = firstLine.IndexOf('\n');
            _preview.Text = (newline < 0 ? firstLine : firstLine[..newline].TrimEnd()).ToString();
        }
        else
        {
            _preview.Text = null;
        }

        var live = !item.Done && item.ThoughtMs is null && item.Cut is null;
        _thought.IsVisible = reasoning.Length > 0;
        ShowReasoning(_thought.IsVisible && (_openThought ?? _folding.StartsOpen(TranscriptPart.Thinking, live: live && item.Text.Length == 0)));
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
        this.FindControl<TextBlock>("ThoughtLabel")!.Text = label;
        AutomationProperties.SetName(_thought, label);
        if (_source != item.Text)
        {
            _source = item.Text;
            ShowMarkdown(item.Text);
        }

        _answer.Opacity = item.Done ? 1 : 0.9;
    }

    /// <summary>Places the following work before the answer actions.</summary>
    /// <param name="activity">The mounted work disclosure.</param>
    internal void SetActivity(Control activity) => this.FindControl<ContentControl>("Activity")!.Content = activity;

    /// <summary>Updates reasoning visibility and its disclosure mark together.</summary>
    /// <param name="open">Whether the reasoning is expanded.</param>
    private void ShowReasoning(bool open)
    {
        _reasoning.IsVisible = open;
        _preview.IsVisible = _folding.ThinkingPreview && !open && _thought.IsVisible && !string.IsNullOrEmpty(_preview.Text);
        this.FindControl<Avalonia.Controls.Shapes.Path>("ThoughtChevron")!.Data = Geometry.Parse(open ? "M6 9 12 15 18 9" : "M9 6 15 12 9 18");
    }

    /// <summary>Names an icon action for keyboard and screen reader use.</summary>
    /// <param name="button">The action.</param>
    /// <param name="label">Localized name.</param>
    private void Label(Button button, string label)
    {
        AutomationProperties.SetName(button, label);
        ToolTip.SetTip(button, label);
    }
}
