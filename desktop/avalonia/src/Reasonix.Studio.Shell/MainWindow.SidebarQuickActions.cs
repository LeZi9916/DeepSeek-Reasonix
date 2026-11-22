using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Lets the reader retain the reference's optional session-row shortcuts.</summary>
    /// <param name="column">The controls settings page.</param>
    private void SessionQuickActionsControl(StackPanel column)
    {
        var card = Card(column, "session.session_row_buttons");
        var input = new CheckBox { Content = MessageCatalog.Get("common.show", Language), IsChecked = _workspacePreferences.SessionQuickActions };
        AutomationProperties.SetAutomationId(input, "action:appearance.session-quick-actions");
        input.IsCheckedChanged += (_, _) =>
        {
            _workspacePreferences = _workspacePreferences with
            {
                SessionQuickActions = input.IsChecked == true,
            };
            Projects.QuickActions = _workspacePreferences.SessionQuickActions;
            if (_connectOnOpen)
            {
                _workspacePreferences.Save();
            }
        };
        card.Children.Add(input);
        card.Children.Add(Detail(MessageCatalog.Get("settings.buttons_shown_on_row_hover", Language)));
    }
}
