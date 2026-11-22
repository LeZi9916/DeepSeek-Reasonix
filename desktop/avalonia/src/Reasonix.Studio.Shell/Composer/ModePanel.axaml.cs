using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;
using System;

namespace Reasonix.Studio.Shell.Composer;

/// <summary>The composer's self-drawn work-mode panel.</summary>
public sealed partial class ModePanel : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="ModePanel"/> class.</summary>
    public ModePanel()
    {
        InitializeComponent();
        AgentOption.Click += (_, _) => Picked?.Invoke("agent");
        PlanOption.Click += (_, _) => Picked?.Invoke("plan");
    }

    /// <summary>Raised when the reader chooses a work mode.</summary>
    public event Action<string>? Picked;

    /// <summary>Applies the current task mode and localized labels.</summary>
    /// <param name="plan">Whether the task starts by planning.</param>
    /// <param name="language">Language for the labels.</param>
    public void Show(bool plan, InterfaceLanguage language)
    {
        ModeTitle.Text = MessageCatalog.Get("composer.work_mode", language);
        ModeContext.Text = MessageCatalog.Get("common.current_task", language);
        AgentTitle.Text = "Agent";
        PlanTitle.Text = "Plan";
        AgentNote.Text = MessageCatalog.Get("composer.run_tasks_with_enabled_tools", language);
        PlanNote.Text = MessageCatalog.Get("composer.plan_steps_before_changing_files", language);
        AgentOption.Classes.Set("current", !plan);
        PlanOption.Classes.Set("current", plan);
        AgentCheck.IsVisible = !plan;
        PlanCheck.IsVisible = plan;
        ErrorText.IsVisible = false;
        AutomationProperties.SetName(AgentOption, "Agent");
        AutomationProperties.SetName(PlanOption, "Plan");
    }

    /// <summary>Enables or disables the choices while a write is in flight.</summary>
    /// <param name="pending">Whether the kernel is applying a choice.</param>
    public void SetPending(bool pending)
    {
        AgentOption.IsEnabled = !pending;
        PlanOption.IsEnabled = !pending;
    }

    /// <summary>Shows a refusal inside the still-open panel.</summary>
    /// <param name="message">Text the kernel or transport supplied.</param>
    public void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = message.Length > 0;
    }
}
