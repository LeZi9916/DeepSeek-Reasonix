using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>Draws the workbench gutter against the native text box's scroll and caret positions.</summary>
public sealed class WorkbenchFileEditor : TextBox
{
    /// <summary>The available width of the line number and folding columns.</summary>
    public static readonly DirectProperty<WorkbenchFileEditor, double> GutterWidthProperty =
        AvaloniaProperty.RegisterDirect<WorkbenchFileEditor, double>(nameof(GutterWidth), editor => editor.GutterWidth);

    /// <summary>The brush used behind the editor and gutter.</summary>
    public static readonly StyledProperty<IBrush?> CanvasBrushProperty =
        AvaloniaProperty.Register<WorkbenchFileEditor, IBrush?>(nameof(CanvasBrush));

    /// <summary>The brush mixed into the active text line.</summary>
    public static readonly StyledProperty<IBrush?> ActiveLineBrushProperty =
        AvaloniaProperty.Register<WorkbenchFileEditor, IBrush?>(nameof(ActiveLineBrush));

    /// <summary>The brush for line numbers outside the active row.</summary>
    public static readonly StyledProperty<IBrush?> LineNumberBrushProperty =
        AvaloniaProperty.Register<WorkbenchFileEditor, IBrush?>(nameof(LineNumberBrush));

    /// <summary>The brush for the caret's line number.</summary>
    public static readonly StyledProperty<IBrush?> ActiveLineNumberBrushProperty =
        AvaloniaProperty.Register<WorkbenchFileEditor, IBrush?>(nameof(ActiveLineNumberBrush));

    /// <summary>Buffer offsets for logical lines, including the empty line after a trailing newline.</summary>
    private readonly List<int> _lineStarts = [0];

    /// <summary>The native control that owns the scrolling offsets.</summary>
    private ScrollViewer? _scroll;

    /// <summary>The native text layout and caret.</summary>
    private TextPresenter? _presenter;

    /// <summary>The current width reserved for line numbering.</summary>
    private double _gutterWidth = 29.2;

    /// <summary>Gets the width reserved for the editor gutter.</summary>
    public double GutterWidth => _gutterWidth;

    /// <summary>Gets or sets the brush behind the editor and gutter.</summary>
    public IBrush? CanvasBrush
    {
        get => GetValue(CanvasBrushProperty);
        set => SetValue(CanvasBrushProperty, value);
    }

    /// <summary>Gets or sets the brush mixed into the active line.</summary>
    public IBrush? ActiveLineBrush
    {
        get => GetValue(ActiveLineBrushProperty);
        set => SetValue(ActiveLineBrushProperty, value);
    }

    /// <summary>Gets or sets the brush for ordinary line numbers.</summary>
    public IBrush? LineNumberBrush
    {
        get => GetValue(LineNumberBrushProperty);
        set => SetValue(LineNumberBrushProperty, value);
    }

    /// <summary>Gets or sets the brush for the caret's line number.</summary>
    public IBrush? ActiveLineNumberBrush
    {
        get => GetValue(ActiveLineNumberBrushProperty);
        set => SetValue(ActiveLineNumberBrushProperty, value);
    }

    /// <summary>Gets the current one-based line of the caret.</summary>
    public int CurrentLine
    {
        get
        {
            var position = _lineStarts.BinarySearch(CaretIndex);
            return position < 0 ? ~position : position + 1;
        }
    }

    /// <inheritdoc/>
    protected override Type StyleKeyOverride => typeof(TextBox);

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (CanvasBrush is { } background)
        {
            context.FillRectangle(background, new Rect(Bounds.Size));
        }

        if (_scroll is not { } scroll || _presenter is not { } presenter || !(LineHeight > 0)
            || presenter.TranslatePoint(default, this) is not { } origin
            || scroll.TranslatePoint(default, this) is not { } viewport)
        {
            return;
        }

        var bottom = Math.Min(Bounds.Height, viewport.Y + scroll.Viewport.Height);
        var right = Math.Min(Bounds.Width, viewport.X + scroll.Viewport.Width);
        var clip = new Rect(0, 0, right, bottom);
        using var clipping = context.PushClip(clip);
        var current = CurrentLine - 1;
        var first = Math.Max(0, (int)Math.Floor(-origin.Y / LineHeight));
        var last = Math.Min(_lineStarts.Count, (int)Math.Ceiling((bottom - origin.Y) / LineHeight));
        var numberRight = GutterWidth - (2 + (FontSize * 0.6)) - 3;
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight);
        for (var index = first; index < last; index++)
        {
            var y = origin.Y + (index * LineHeight);
            if (index == current && ActiveLineBrush is { } active && right > GutterWidth)
            {
                using var opacity = context.PushOpacity(0.55);
                context.FillRectangle(active, new Rect(GutterWidth, y, right - GutterWidth, LineHeight));
            }

            var ink = index == current ? ActiveLineNumberBrush : LineNumberBrush;
            if (ink is not null)
            {
                var label = new FormattedText(
                    (index + 1).ToString(CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    FontSize,
                    ink);
                context.DrawText(label, new Point(numberRight - label.Width, y + ((LineHeight - label.Height) / 2)));
            }
        }
    }

    /// <inheritdoc/>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_scroll is not null)
        {
            _scroll.ScrollChanged -= OnEditorScrollChanged;
        }

        base.OnApplyTemplate(e);
        _presenter = e.NameScope.Find<TextPresenter>("PART_TextPresenter");
        _scroll = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
        if (_scroll is not null)
        {
            _scroll.ScrollChanged += OnEditorScrollChanged;
        }

        MeasureGutter();
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            IndexLines();
            MeasureGutter();
        }
        else if (change.Property == FontSizeProperty)
        {
            MeasureGutter();
        }
        else if (change.Property == CaretIndexProperty || change.Property == CanvasBrushProperty
            || change.Property == ActiveLineBrushProperty || change.Property == LineNumberBrushProperty
            || change.Property == ActiveLineNumberBrushProperty)
        {
            InvalidateVisual();
        }
    }

    /// <summary>Refreshes row positions when the native text area scrolls or resizes.</summary>
    /// <param name="sender">The text area's scroll viewer.</param><param name="e">The changed scroll geometry.</param>
    private void OnEditorScrollChanged(object? sender, ScrollChangedEventArgs e) => InvalidateVisual();

    /// <summary>Counts the digit widths used by the reference gutter and tracks the editor font scale.</summary>
    private void MeasureGutter()
    {
        var digitWidth = FontSize * 0.6;
        var digits = _lineStarts.Count.ToString(CultureInfo.InvariantCulture).Length;
        SetAndRaise(GutterWidthProperty, ref _gutterWidth, Math.Max(20, 8 + (digitWidth * digits)) + 2 + digitWidth);
        SetCurrentValue(LineHeightProperty, FontSize * 1.65);
        InvalidateVisual();
    }

    /// <summary>Indexes line starts without modifying the buffer's newline convention.</summary>
    private void IndexLines()
    {
        _lineStarts.Clear();
        _lineStarts.Add(0);
        var text = Text ?? string.Empty;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
            {
                index++;
            }

            if (text[index] is '\r' or '\n')
            {
                _lineStarts.Add(index + 1);
            }
        }
    }
}
