using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;
using System;

namespace Reasonix.Studio.Shell.Composer;

/// <summary>The composer's self-drawn execution-permission panel.</summary>
public sealed partial class PolicyPanel : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="PolicyPanel"/> class.</summary>
    public PolicyPanel()
    {
        InitializeComponent();
        DontAskOption.Click += (_, _) => Picked?.Invoke("dontAsk");
        AskOption.Click += (_, _) => Picked?.Invoke("ask");
        AutoOption.Click += (_, _) => Picked?.Invoke("auto");
        YoloOption.Click += (_, _) => Picked?.Invoke("yolo");
        BoundaryOption.Click += (_, _) => BoundaryRequested?.Invoke();
    }

    /// <summary>Raised when the reader chooses an approval mode.</summary>
    public event Action<string>? Picked;

    /// <summary>Raised when the reader asks for the sandbox settings destination.</summary>
    public event Action? BoundaryRequested;

    /// <summary>Applies the current approval mode and localized labels.</summary>
    /// <param name="mode">Kernel approval mode.</param>
    /// <param name="language">Language for the labels.</param>
    public void Show(string mode, InterfaceLanguage language)
    {
        PolicyTitle.Text = MessageCatalog.Get("safety.execution_permissions", language);
        PolicyContext.Text = MessageCatalog.Get("common.current_task", language);
        DontAskTitle.Text = MessageCatalog.Get("safety.do_not_ask", language);
        DontAskNote.Text = MessageCatalog.Get("safety.no_approval_prompts_anything_needing", language);
        AskTitle.Text = MessageCatalog.Get("safety.ask", language);
        AskNote.Text = MessageCatalog.Get("safety.asks_before_every_action", language);
        AutoTitle.Text = MessageCatalog.Get("safety.auto_approve", language);
        AutoNote.Text = MessageCatalog.Get("safety.low_risk_steps_proceed_writes", language);
        RiskNote.Text = MessageCatalog.Get("common.high_risk", language);
        YoloTitle.Text = MessageCatalog.Get("safety.allow_all", language);
        YoloNote.Text = MessageCatalog.Get("safety.stops_asking_only_for_a", language);
        PolicyNote.Text = MessageCatalog.Get("safety.approvals_decide_when_to_ask", language);
        BoundaryTitle.Text = MessageCatalog.Get("safety.sandbox_and_runtime_boundary", language);
        BoundaryNote.Text = MessageCatalog.Get("safety.view_current_isolation_and_network", language);
        Apply(mode);
        ErrorText.IsVisible = false;
        AutomationProperties.SetName(DontAskOption, DontAskTitle.Text);
        AutomationProperties.SetName(AskOption, AskTitle.Text);
        AutomationProperties.SetName(AutoOption, AutoTitle.Text);
        AutomationProperties.SetName(YoloOption, YoloTitle.Text);
    }

    /// <summary>Enables or disables the choices while a write is in flight.</summary>
    /// <param name="pending">Whether the kernel is applying a choice.</param>
    public void SetPending(bool pending)
    {
        DontAskOption.IsEnabled = !pending;
        AskOption.IsEnabled = !pending;
        AutoOption.IsEnabled = !pending;
        YoloOption.IsEnabled = !pending;
    }

    /// <summary>Shows a refusal inside the still-open panel.</summary>
    /// <param name="message">Text the kernel or transport supplied.</param>
    public void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = message.Length > 0;
    }

    /// <summary>Marks the current approval mode without moving any label.</summary>
    /// <param name="mode">Kernel approval mode.</param>
    private void Apply(string mode)
    {
        DontAskOption.Classes.Set("current", mode == "dontAsk");
        AskOption.Classes.Set("current", mode == "ask");
        AutoOption.Classes.Set("current", mode == "auto");
        YoloOption.Classes.Set("current", mode == "yolo");
        DontAskCheck.IsVisible = mode == "dontAsk";
        AskCheck.IsVisible = mode == "ask";
        AutoCheck.IsVisible = mode == "auto";
        YoloCheck.IsVisible = mode == "yolo";
    }
}
