using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.State;
using System;

namespace Reasonix.Studio.Shell.Composer;

/// <summary>Displays one host-reported plan step in the expanded strip.</summary>
public sealed partial class PlanStepRow : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="PlanStepRow"/> class.</summary>
    public PlanStepRow() => InitializeComponent();

    /// <summary>Updates the row without replacing its identity in the list.</summary>
    /// <param name="step">The acknowledged plan entry.</param><param name="ordinal">Its position.</param>
    /// <param name="current">Whether the step is in hand.</param><param name="running">Whether the plan is running.</param>
    /// <param name="language">The reader's language.</param>
    internal void Show(PlanStep step, int ordinal, bool current, bool running, InterfaceLanguage language)
    {
        var done = step.Status == TodoStatus.Completed;
        var moving = current && running;
        Margin = new Thickness(Math.Clamp(step.Level ?? 0, 0, 4) * 12, 0, 0, 0);
        Label.Text = step.Status == TodoStatus.InProgress && !string.IsNullOrEmpty(step.ActiveForm) ? step.ActiveForm : step.Text;
        Label.FontWeight = current ? FontWeight.Medium : FontWeight.Normal;
        Label[!TextBlock.ForegroundProperty] = StudioTypography.Resource(done ? "MutedBrush" : "TextBrush");
        Mark.IsVisible = !moving;
        Mark.BorderThickness = new Thickness(done ? 0 : 1.5);
        if (done)
        {
            Mark[!Border.BackgroundProperty] = StudioTypography.Resource("OkBrush");
        }
        else
        {
            Mark.ClearValue(Border.BackgroundProperty);
        }

        Done.IsVisible = done;
        Moving.IsVisible = moving;
        Ordinal.IsVisible = !done && !moving;
        Ordinal.Text = Format.Count(ordinal, language);
        AutomationProperties.SetName(Mark, MessageCatalog.Get(done ? "plan.step.done" : current ? "plan.step.now" : "plan.step.todo", language));
    }
}
