using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Host;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The current machine's workspace presentation preferences.</summary>
    private WorkspacePreferences _workspacePreferences = new();

    /// <summary>Lets the reader explicitly choose whether idle tabs may be folded away.</summary>
    /// <param name="column">The controls settings page.</param>
    private void SessionLimitControl(StackPanel column)
    {
        var card = Card(column, "settings.session_tabs_kept_open");
        var input = new NumericUpDown { Minimum = 0, Maximum = int.MaxValue, Value = _workspacePreferences.OpenSessionLimit, Increment = 1, FormatString = "0", Width = 140, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        Avalonia.Automation.AutomationProperties.SetAutomationId(input, "action:settings.open-session-limit");
        input.ValueChanged += (_, _) =>
        {
            _workspacePreferences = _workspacePreferences with
            {
                OpenSessionLimit = (int)(input.Value ?? 0),
            };
            if (_connectOnOpen)
            {
                _workspacePreferences.Save();
            }

            if (_navigation is not null)
            {
                _navigation.Limit = _workspacePreferences.OpenSessionLimit;
            }
        };
        card.Children.Add(input);
        card.Children.Add(Detail(MessageCatalog.Get("settings.default_0_nothing_is_folded", Language)));
    }
}
