using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Connects the workbench action and reflects its current view.</summary>
    private void InitializeChromeViews()
    {
        ViewSwitch.Click += async (_, _) => await ToggleChromeWorkbenchAsync().ConfigureAwait(true);
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
        ViewSwitch.Classes.Set("active-view", WorkbenchPanel.IsVisible);
        AutomationProperties.SetName(ViewSwitch, MessageCatalog.Get("common.workbench", Language));
        ToolTip.SetTip(ViewSwitch, MessageCatalog.Get(WorkbenchPanel.IsVisible ? "app.close_workbench" : "app.open_workbench", Language));
        var count = FileTabs.Children.Count(tab => tab.Tag is not null && !tab.Classes.Contains("launcher"));
        ChromeWorkbenchCount.IsVisible = count > 0;
        ChromeWorkbenchCountLabel.Text = Format.Count(count, Language);
    }
}
