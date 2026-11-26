using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;

namespace Reasonix.Studio.Shell.Composer;

/// <summary>Draws accepted completion labels against the scrolling native text layout.</summary>
internal sealed class ComposerDraftBox : TextBox
{
    /// <summary>The accepted labels still present in this draft.</summary>
    private readonly List<Label> _labels = [];

    /// <summary>The previous draft used to move label offsets across edits.</summary>
    private string _previous = string.Empty;

    /// <summary>The template's text layout and scroller.</summary>
    private TextPresenter? _presenter;

    /// <summary>The native viewport holding the text presenter.</summary>
    private ScrollViewer? _viewport;

    /// <inheritdoc/>
    protected override Type StyleKeyOverride => typeof(TextBox);

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_labels.Count == 0 || _presenter is not { } presenter || _viewport is not { } viewport
            || presenter.TranslatePoint(default, this) is not { } origin
            || viewport.TranslatePoint(default, this) is not { } clip)
        {
            return;
        }

        using var clipping = context.PushClip(new Rect(clip, viewport.Viewport));
        foreach (var label in _labels)
        {
            if (!this.TryFindResource(label.Subagent ? "LabelAgentWashBrush" : "BrandWashBrush", out var resource) || resource is not IBrush brush)
            {
                continue;
            }

            foreach (var rect in presenter.TextLayout.HitTestTextRange(label.Start, label.Length))
            {
                var bounds = new Rect(rect.Position + new Vector(origin.X, origin.Y), rect.Size).Inflate(2);
                context.DrawRectangle(brush, null, bounds, 5, 5);
            }
        }
    }

    /// <summary>Marks a skill or subagent completion explicitly selected from the menu.</summary>
    /// <param name="start">Its UTF-16 offset in the text box.</param><param name="length">The length of the inserted label.</param>
    /// <param name="subagent">Whether to use the subagent palette.</param>
    internal void MarkCompletion(int start, int length, bool subagent)
    {
        _labels.Add(new Label(start, length, subagent));
        InvalidateVisual();
    }

    /// <summary>Releases the selected labels before mounting another draft.</summary>
    internal void ClearCompletions()
    {
        _labels.Clear();
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_viewport is not null)
        {
            _viewport.ScrollChanged -= OnDraftScroll;
        }

        base.OnApplyTemplate(e);
        _presenter = e.NameScope.Find<TextPresenter>("PART_TextPresenter");
        _viewport = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
        if (_viewport is not null)
        {
            _viewport.ScrollChanged += OnDraftScroll;
        }
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            var text = Text ?? string.Empty;
            MoveLabels(text);
            _previous = text;
        }
    }

    /// <summary>Moves labels that survived a text edit and releases any the edit touched.</summary>
    /// <param name="next">The edited draft.</param>
    private void MoveLabels(string next)
    {
        if (_labels.Count == 0 || _previous == next)
        {
            return;
        }

        var prefix = 0;
        while (prefix < Math.Min(_previous.Length, next.Length) && _previous[prefix] == next[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < Math.Min(_previous.Length, next.Length) - prefix && _previous[^(suffix + 1)] == next[^(suffix + 1)])
        {
            suffix++;
        }

        var oldEnd = _previous.Length - suffix;
        var delta = next.Length - _previous.Length;
        for (var index = _labels.Count - 1; index >= 0; index--)
        {
            var label = _labels[index];
            if (label.Start >= oldEnd)
            {
                _labels[index] = label with
                {
                    Start = label.Start + delta,
                };
            }
            else if (label.Start + label.Length > prefix)
            {
                _labels.RemoveAt(index);
            }
        }

        InvalidateVisual();
    }

    /// <summary>Moves the labels with the same native viewport as their text.</summary>
    /// <param name="sender">The template scroller.</param><param name="args">The changed viewport.</param>
    private void OnDraftScroll(object? sender, ScrollChangedEventArgs args) => InvalidateVisual();

    /// <summary>The range and appearance of a selected completion label.</summary>
    /// <param name="Start">Its UTF-16 start offset.</param><param name="Length">Its length.</param><param name="Subagent">Whether it is a subagent label.</param>
    private readonly record struct Label(int Start, int Length, bool Subagent);
}
