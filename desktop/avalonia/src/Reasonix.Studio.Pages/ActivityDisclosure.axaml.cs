using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using System;
using Path = Avalonia.Controls.Shapes.Path;

namespace Reasonix.Studio.Pages;

/// <summary>A work summary with a retained body for search, selection and output expansion.</summary>
internal sealed partial class ActivityDisclosure : UserControl
{
    /// <summary>The retained disclosure body.</summary>
    private readonly Control _body;

    /// <summary>Records explicitly revealed content.</summary>
    private readonly Action<bool> _changed;

    /// <summary>Initializes a new instance of the <see cref="ActivityDisclosure"/> class.</summary>
    /// <param name="body">The mounted activity rows.</param>
    /// <param name="label">Canonical activity counts.</param>
    /// <param name="open">Initial disclosure state.</param>
    /// <param name="ink">Semantic status brush.</param>
    /// <param name="changed">Remembers an explicit reader choice.</param>
    internal ActivityDisclosure(Control body, string label, bool open, string ink, Action<bool> changed)
    {
        AvaloniaXamlLoader.Load(this);
        _body = body;
        _changed = changed;
        this.FindControl<TextBlock>("Summary")!.Text = label;
        this.FindControl<ContentControl>("Body")!.Content = body;
        this.FindControl<Path>("Status")!.Bind(Shape.StrokeProperty, new DynamicResourceExtension(ink));
        var toggle = this.FindControl<Button>("Toggle")!;
        AutomationProperties.SetName(toggle, label);
        SetOpen(open);
        toggle.Click += (_, _) =>
        {
            SetOpen(!body.IsVisible);
            changed(body.IsVisible);
        };
    }

    /// <summary>Reveals a search hit within this disclosure.</summary>
    internal void Reveal()
    {
        SetOpen(true);
        _changed(true);
    }

    /// <summary>Updates the mounted body and its chevron together.</summary>
    /// <param name="value">Whether the body is expanded.</param>
    internal void SetOpen(bool value)
    {
        _body.IsVisible = value;
        this.FindControl<Path>("Chevron")!.Data = Geometry.Parse(value ? "M6 9 12 15 18 9" : "M9 6 15 12 9 18");
    }
}
