using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using Path = Avalonia.Controls.Shapes.Path;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>A hairline disclosure that reveals further theme cards, as the reference draws it.</summary>
public sealed class ThemeMore : Border
{
    /// <summary>The presenter that reveals or hides the further cards.</summary>
    private readonly ContentPresenter _body;

    /// <summary>The disclosure arrow, rotated between closed and open.</summary>
    private readonly Path _chevron;

    /// <summary>Whether the further cards are currently shown.</summary>
    private bool _expanded;

    /// <summary>Initializes a new instance of the <see cref="ThemeMore"/> class.</summary>
    /// <param name="title">Primary summary line.</param>
    /// <param name="description">Secondary summary line.</param>
    /// <param name="count">Count shown at the end of the summary.</param>
    /// <param name="body">Cards revealed while expanded.</param>
    public ThemeMore(string title, string description, string count, Control body)
    {
        BorderThickness = new Thickness(0, 1, 0, 1);
        Bind(BorderBrushProperty, this.GetResourceObservable("HairBrush"));
        Margin = new Thickness(0, 4, 0, 0);

        var heading = new StackPanel { Spacing = 2 };
        var name = new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center };
        name.Bind(TextBlock.ForegroundProperty, name.GetResourceObservable("TextBrush"));
        var detail = new TextBlock { Text = description, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        detail.Bind(TextBlock.ForegroundProperty, detail.GetResourceObservable("FaintBrush"));
        heading.Children.Add(name);
        heading.Children.Add(detail);

        var total = new TextBlock { Text = count, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        total.Bind(TextBlock.ForegroundProperty, total.GetResourceObservable("FaintBrush"));

        _chevron = new Path
        {
            Data = Geometry.Parse("M9 6l6 6-6 6"),
            StrokeThickness = 1.4,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Stretch = Stretch.Uniform,
            Width = 10,
            Height = 10,
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
        };
        _chevron.Bind(Shape.StrokeProperty, _chevron.GetResourceObservable("MutedBrush"));

        var summary = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("10,*,Auto"),
            ColumnSpacing = 9,
        };
        summary.Children.Add(_chevron);
        Grid.SetColumn(heading, 1);
        summary.Children.Add(heading);
        Grid.SetColumn(total, 2);
        summary.Children.Add(total);

        var toggle = new Button
        {
            Content = summary,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2, 11),
            MinHeight = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(0),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        toggle.Click += (_, _) => IsExpanded = !IsExpanded;

        _body = new ContentPresenter { Content = body, Margin = new Thickness(0, 5, 0, 15) };

        var layout = new StackPanel();
        layout.Children.Add(toggle);
        layout.Children.Add(_body);
        Child = layout;
        IsExpanded = true;
    }

    /// <summary>Gets or sets a value indicating whether the further cards are shown.</summary>
    public bool IsExpanded
    {
        get
        {
            return _expanded;
        }

        set
        {
            if (_expanded == value)
            {
                return;
            }

            _expanded = value;
            _body.IsVisible = value;
            _chevron.RenderTransform = new RotateTransform(value ? 90 : 0);
        }
    }
}
