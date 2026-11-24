using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media.Transformation;
using System;
using Path = Avalonia.Controls.Shapes.Path;

namespace Reasonix.Studio.Pages;

/// <summary>A work summary with a retained body for search, selection and output expansion.</summary>
internal sealed partial class ActivityDisclosure : UserControl
{
    /// <summary>The chevron targets that the motion style interpolates.</summary>
    private static readonly TransformOperations s_collapsed = TransformOperations.Parse("rotate(-90deg)");

    /// <summary>The settled direction of the expanded chevron.</summary>
    private static readonly TransformOperations s_expanded = TransformOperations.Parse("rotate(0deg)");

    /// <summary>The retained disclosure body.</summary>
    private readonly DisclosureMotion _body;

    /// <summary>Records explicitly revealed content.</summary>
    private readonly Action<bool> _changed;

    /// <summary>Initializes a new instance of the <see cref="ActivityDisclosure"/> class.</summary>
    /// <param name="body">The mounted activity rows.</param>
    /// <param name="label">Canonical activity counts.</param>
    /// <param name="open">Initial disclosure state.</param>
    /// <param name="ink">Semantic status brush.</param>
    /// <param name="changed">Remembers an explicit reader choice.</param>
    /// <param name="title">Optional expanded heading for a tool activity group.</param>
    internal ActivityDisclosure(Control body, string label, bool open, string ink, Action<bool> changed, string? title = null)
    {
        AvaloniaXamlLoader.Load(this);
        _body = this.FindControl<DisclosureMotion>("Body")!;
        body.IsVisible = true;
        _body.Child = body;
        _changed = changed;
        this.FindControl<TextBlock>("Summary")!.Text = label;
        this.FindControl<Path>("Status")!.Bind(Shape.StrokeProperty, new DynamicResourceExtension(ink));
        var toggle = this.FindControl<Button>("Toggle")!;
        AutomationProperties.SetName(toggle, label);
        toggle.Classes.Set("activity-group", title is not null);
        this.FindControl<TextBlock>("TitleLabel")!.Text = title;
        _liveClock.Tick += (_, _) => UpdateLiveCaption();
        AttachedToVisualTree += (_, _) => RefreshLiveHeading();
        DetachedFromVisualTree += (_, _) => _liveClock.Stop();
        SetOpen(open);
        toggle.Click += (_, _) =>
        {
            SetOpen(!_body.Expanded, animate: true);
            changed(_body.Expanded);
        };
    }

    /// <summary>Applies the disclosure default without animation.</summary>
    /// <param name="value">Whether the body is expanded.</param>
    internal void SetOpen(bool value) => SetOpen(value, animate: false);

    /// <summary>Reveals a search hit within this disclosure.</summary>
    internal void Reveal()
    {
        SetOpen(true);
        _changed(true);
    }

    /// <summary>Updates the mounted body and its chevron together.</summary>
    /// <param name="value">Whether the body is expanded.</param>
    /// <param name="animate">Whether the reader toggled the row.</param>
    private void SetOpen(bool value, bool animate)
    {
        _body.SetExpanded(value, animate);
        this.FindControl<Button>("Toggle")!.Classes.Set("open", value);
        var toggle = this.FindControl<Button>("Toggle")!;
        var title = this.FindControl<TextBlock>("TitleLabel")!;
        title.IsVisible = title.Text is not null && (value || toggle.Classes.Contains("activity-failed"));
        RefreshLiveHeading();
        var chevron = this.FindControl<Viewbox>("Chevron")!;
        chevron.Classes.Set("open", value);
        chevron.RenderTransform = value ? s_expanded : s_collapsed;
    }
}
