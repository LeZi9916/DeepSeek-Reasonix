using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System.Linq;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Connects the docked strip's viewport and outside-pointer dismissal.</summary>
    private void InitializePlanStrip()
    {
        SizeChanged += (_, e) => ComposerPlan.SetViewport(e.NewSize.Height);
        AddHandler(
            PointerPressedEvent,
            (_, e) =>
            {
                if (ComposerPlan.IsExpanded && e.Source is Visual origin && !origin.GetSelfAndVisualAncestors().Contains(ComposerPlan))
                {
                    ComposerPlan.Collapse(restoreFocus: false);
                }
            },
            RoutingStrategies.Tunnel);
    }

    /// <summary>Shows the canonical plan except while a decision occupies the transcript's end.</summary>
    /// <param name="state">The current acknowledged transcript state.</param>
    private void ShowPlanStrip(SessionState state)
    {
        var waiting = state.Plan.Count > 0 && state.Items.Any(item => item is ApprovalItem { Open: true } or AskItem { Answered: null, Outcome: null });
        ComposerPlan.Show(state.Plan, state.Running, waiting, _workspacePreferences.ShowPlanProgress, Language);
    }

    /// <summary>Adds the device-local preference for the collapsed plan's progress.</summary>
    /// <param name="card">The conversation process settings.</param>
    private void AddPlanProgressSetting(StackPanel card)
    {
        var option = new ToggleSwitch { IsChecked = _workspacePreferences.ShowPlanProgress };
        AutomationProperties.SetAutomationId(option, "action:appearance.plan-progress");
        AddRow(card, "plan.show_collapsed_plan_progress", option, MessageCatalog.Get("settings.plan_progress_beside_title", Language));
        option.IsCheckedChanged += (_, _) =>
        {
            _workspacePreferences = _workspacePreferences with
            {
                ShowPlanProgress = option.IsChecked == true,
            };
            if (_connectOnOpen)
            {
                _workspacePreferences.Save();
            }

            ShowPlanStrip(Conversation.State);
        };
    }
}
