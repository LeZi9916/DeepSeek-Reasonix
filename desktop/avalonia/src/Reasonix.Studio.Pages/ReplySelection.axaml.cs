using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;

namespace Reasonix.Studio.Pages;

/// <summary>Places the quote and copy tools at a completed reply's settled text selection.</summary>
internal sealed partial class ReplySelection : Canvas
{
    /// <summary>The selected reply block while pointer input settles.</summary>
    private SelectableTextBlock? _source;

    /// <summary>The scroller whose upper edge bounds the toolbar placement.</summary>
    private ScrollViewer? _scroll;

    /// <summary>The reference delay between mouse-up and the appearance of the toolbar.</summary>
    private IDisposable? _delay;

    /// <summary>The text that was selected when the toolbar appeared.</summary>
    private string _passage = string.Empty;

    /// <summary>Initializes a new instance of the <see cref="ReplySelection"/> class.</summary>
    public ReplySelection()
    {
        InitializeComponent();
        QuoteButton.Click += (_, _) =>
        {
            var passage = _passage;
            Clear();
            if (passage.Length > 0)
            {
                QuoteRequested?.Invoke(passage);
            }
        };
        CopyButton.Click += async (_, _) =>
        {
            var passage = _passage;
            Clear();
            if (passage.Length == 0)
            {
                return;
            }

            var copied = false;
            try
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                {
                    await clipboard.SetTextAsync(passage).ConfigureAwait(true);
                    copied = true;
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                // The window or its platform clipboard can disappear during a write.
            }

            CopyFinished?.Invoke(copied);
        };
        DetachedFromVisualTree += (_, _) => Clear();
    }

    /// <summary>Raised when the reader asks to quote the selected text.</summary>
    internal event Action<string>? QuoteRequested;

    /// <summary>Raised with the platform clipboard outcome.</summary>
    internal event Action<bool>? CopyFinished;

    /// <summary>Checks whether a click came from the retained toolbar.</summary>
    /// <param name="source">The visual under a pointer press.</param>
    /// <returns>Whether the toolbar owns the pointer action.</returns>
    internal bool ContainsToolbar(Visual source) => ReferenceEquals(Toolbar, source) || Toolbar.IsVisualAncestorOf(source);

    /// <summary>Waits until the pointer selection has settled before showing its tools.</summary>
    /// <param name="source">The selectable block of a completed reply.</param>
    /// <param name="scroll">The transcript scroller.</param>
    /// <param name="language">The reader's current language.</param>
    internal void Settle(SelectableTextBlock source, ScrollViewer scroll, InterfaceLanguage language)
    {
        Clear();
        if (source.SelectionStart == source.SelectionEnd)
        {
            return;
        }

        _source = source;
        _scroll = scroll;
        source.PropertyChanged += OnSelectionChanged;
        source.DetachedFromVisualTree += OnSourceDetached;
        _delay = DispatcherTimer.RunOnce(() => Show(language), TimeSpan.FromMilliseconds(120));
    }

    /// <summary>Hides the toolbar when the reader moves, scrolls or leaves the selection.</summary>
    internal void Clear()
    {
        _delay?.Dispose();
        _delay = null;
        if (_source is { } source)
        {
            source.PropertyChanged -= OnSelectionChanged;
            source.DetachedFromVisualTree -= OnSourceDetached;
        }

        _source = null;
        _scroll = null;
        _passage = string.Empty;
        Toolbar.IsVisible = false;
        Toolbar.Classes.Remove("above");
        Toolbar.Classes.Remove("below");
    }

    /// <summary>Uses the shaped selection bounds to choose an edge of the toolbar.</summary>
    /// <param name="language">The reader's current language.</param>
    private void Show(InterfaceLanguage language)
    {
        if (_source is not { IsEffectivelyVisible: true } source || _scroll is not { } scroll)
        {
            Clear();
            return;
        }

        var passage = source.SelectedText?.Trim() ?? string.Empty;
        var start = Math.Min(source.SelectionStart, source.SelectionEnd);
        var length = Math.Abs(source.SelectionEnd - source.SelectionStart);
        var rectangles = source.TextLayout.HitTestTextRange(start, length).ToArray();
        if (passage.Length == 0 || rectangles.Length == 0)
        {
            Clear();
            return;
        }

        var range = rectangles.Aggregate((first, next) => first.Union(next));
        var top = source.TranslatePoint(range.TopLeft + new Vector(source.Padding.Left, source.Padding.Top), this);
        var bottom = source.TranslatePoint(range.BottomLeft + new Vector(source.Padding.Left, source.Padding.Top), this);
        var floor = scroll.TranslatePoint(default, this);
        if (top is not { } from || bottom is not { } to || floor is not { } viewport || to.Y < viewport.Y || from.Y >= Bounds.Height)
        {
            Clear();
            return;
        }

        _passage = passage;
        var below = from.Y - viewport.Y < 40;
        SetLeft(Toolbar, from.X);
        SetTop(Toolbar, below ? to.Y + 6 : from.Y - 6 - Toolbar.Height);
        QuoteLabel.Text = MessageCatalog.Get(MessageKey.Common.QuoteSelectedText, language);
        CopyLabel.Text = MessageCatalog.Get(MessageKey.Chat.CopySelectedText, language);
        AutomationProperties.SetName(Toolbar, MessageCatalog.Get("chat.selected_text", language));
        Toolbar.BoxShadow = BoxShadows.Parse(ActualThemeVariant == ThemeVariant.Dark
            ? "inset 0 1 0 #0FFFFFFF, 0 22 60 -22 #D9000000, 0 6 18 -6 #59000000"
            : "0 18 44 -18 #33171E2D, 0 4 12 -8 #24171E2D");
        Toolbar.IsVisible = true;
        Toolbar.Classes.Set(below ? "below" : "above", true);
    }

    /// <summary>Retires the toolbar when its selection has collapsed.</summary>
    /// <param name="sender">The selected text block.</param>
    /// <param name="change">The property change on that text block.</param>
    private void OnSelectionChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if ((change.Property == SelectableTextBlock.SelectionStartProperty || change.Property == SelectableTextBlock.SelectionEndProperty)
            && sender is SelectableTextBlock text && text.SelectionStart == text.SelectionEnd)
        {
            Clear();
        }
    }

    /// <summary>Does not hold the text of a reply retired by navigation or rebuilding.</summary>
    /// <param name="sender">The detached text block.</param>
    /// <param name="args">The visual-tree detachment.</param>
    private void OnSourceDetached(object? sender, VisualTreeAttachmentEventArgs args) => Clear();
}
