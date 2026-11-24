using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Connects the same direct pane actions shown on the reference window bar.</summary>
    private void InitializeChromeViews()
    {
        ChromeAnalysis.Click += (_, _) => ShowAnalysis(!AnalysisPanel.IsVisible);
        ChromeAnalysis.PropertyChanged += (_, change) =>
        {
            if (change.Property == IsVisibleProperty)
            {
                DrawChromeViews();
            }
        };
        ViewSwitch.Click += async (_, _) => await ToggleChromeWorkbenchAsync().ConfigureAwait(true);
        AnalysisPanel.PropertyChanged += (_, change) =>
        {
            if (change.Property == IsVisibleProperty)
            {
                DrawChromeViews();
            }
        };
        WorkbenchPanel.PropertyChanged += (_, change) =>
        {
            if (change.Property == IsVisibleProperty)
            {
                DrawChromeViews();
            }
        };
        FileTabs.Children.CollectionChanged += (_, _) => DrawChromeViews();
    }

    /// <summary>Opens the workbench directly or returns to the conversation.</summary>
    /// <returns>The workbench navigation.</returns>
    private Task ToggleChromeWorkbenchAsync()
    {
        if (!WorkbenchPanel.IsVisible || _workbenchDismiss is not null)
        {
            return OpenWorkbenchAsync();
        }

        return DismissWorkbenchAsync();
    }

    /// <summary>Reflects the active side view and number of retained workbench surfaces.</summary>
    private void DrawChromeViews()
    {
        ChromeActions.Margin = ChromeAnalysis.IsVisible ? default : new Thickness(8, 0, 0, 0);
        ChromeAnalysis.Classes.Set("active-view", AnalysisPanel.IsVisible);
        ViewSwitch.Classes.Set("active-view", WorkbenchPanel.IsVisible);
        AutomationProperties.SetName(ChromeAnalysis, MessageCatalog.Get("common.run_analysis", Language));
        AutomationProperties.SetName(ViewSwitch, MessageCatalog.Get("common.workbench", Language));
        ToolTip.SetTip(ChromeAnalysis, MessageCatalog.Get(AnalysisPanel.IsVisible ? "app.close_run_analysis" : "app.open_run_analysis", Language));
        ToolTip.SetTip(ViewSwitch, MessageCatalog.Get(WorkbenchPanel.IsVisible ? "app.close_workbench" : "app.open_workbench", Language));
        var count = FileTabs.Children.Count(tab => tab.Tag is not null && !tab.Classes.Contains("launcher"));
        ChromeWorkbenchCount.IsVisible = count > 0;
        ChromeWorkbenchCountLabel.Text = Format.Count(count, Language);
    }
}
