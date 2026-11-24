using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Globalization;
using System.Linq;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Shows the reference mark or check during the connection handshake.</summary>
    /// <param name="checking">Whether provider discovery is still running.</param>
    /// <param name="connected">Whether the discovery succeeded.</param>
    private void SetConnectFeedback(bool checking, bool connected = false)
    {
        ConnectProvider.Classes.Set("checking", checking);
        ConnectProvider.Classes.Set("connected", connected);
        ConnectSpinner.IsVisible = checking;
        ConnectCheck.IsVisible = connected;
        ConnectLabel.IsVisible = !checking && !connected;
    }

    /// <summary>Confirms successful discovery in the connect step before advancing.</summary>
    /// <param name="probe">The successful provider reply.</param>
    private void ShowConnectedProbe(ProviderProbe probe)
    {
        var vendor = SetupServices.Children.OfType<RadioButton>().FirstOrDefault(radio => radio.IsChecked == true) is { } selection
            ? AutomationProperties.GetName(selection)
            : null;
        SetupFoundLabel.Text = MessageCatalog.Get(
            "onboarding.connected_to_name_n_models",
            Language,
            ("name", vendor ?? ServiceName.Text ?? string.Empty),
            ("n", probe.Models.Length.ToString(CultureInfo.CurrentCulture)));
        SetConnectFeedback(checking: false, connected: true);
        SetupFound.IsVisible = true;
        SetupFound.Classes.Add("enter");
    }

    /// <summary>Shows or clears the failed request and marks the visible key field.</summary>
    /// <param name="message">The localized refusal or null for a new attempt.</param>
    private void SetSetupError(string? message)
    {
        var visible = !string.IsNullOrEmpty(message);
        SetupError.Text = message;
        SetupError.IsVisible = visible;
        SetupError.Classes.Set("shown", visible);
        ServiceKey.Classes.Set("setup-invalid", visible && ServiceKey.IsVisible);
    }
}
