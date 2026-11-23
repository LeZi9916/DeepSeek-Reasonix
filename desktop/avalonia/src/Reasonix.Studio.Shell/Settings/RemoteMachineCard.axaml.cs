using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Pages.I18n;
using System.Text.Json.Nodes;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Displays a configured machine and the actions available in its current state.</summary>
public sealed partial class RemoteMachineCard : SettingsFormView
{
    /// <summary>Initializes a new instance of the <see cref="RemoteMachineCard"/> class.</summary>
    public RemoteMachineCard()
    {
        InitializeComponent();
        Actions.Opened += (_, _) => More.Classes.Add("menu-open");
        Actions.Closed += (_, _) => More.Classes.Remove("menu-open");
        Keep.Click += (_, _) =>
        {
            Confirm.IsVisible = false;
            More.Focus();
        };
    }

    /// <summary>Gets the machine's overflow menu.</summary>
    internal MenuFlyout Actions => (MenuFlyout)More.Flyout!;

    /// <summary>Shows the host's address, connection state and saved workspaces.</summary>
    /// <param name="entry">The host-owned address-book row.</param>
    /// <param name="language">The settings language.</param><param name="hidden">Whether the rail hides this row.</param>
    internal void Show(JsonObject entry, InterfaceLanguage language, bool hidden)
    {
        Translate(language);
        var name = (string?)entry["name"] ?? string.Empty;
        var disabled = (bool?)entry["disabled"] == true;
        var status = disabled ? "off" : (string?)entry["status"] ?? "idle";
        Machine.Text = name;
        Target.Text = (string?)entry["target"] ?? (string?)entry["host"] ?? (string?)entry["distro"];
        Target.IsVisible = !string.IsNullOrEmpty(Target.Text);
        Identity.Opacity = disabled ? 0.6 : 1;
        StatusLabel.Text = status switch
        {
            "idle" => Text("common.not_connected"),
            "connecting" => Text("remote.connecting_2"),
            "connected" => Text("remote.connected"),
            "reconnecting" => Text("remote.reconnecting"),
            "degraded" => Text("remote.a_forward_did_not_attach"),
            "stopped" => Text("remote.disconnected"),
            "off" => Text("common.suspended"),
            _ => status,
        };
        Status.Classes.Set("connected", status == "connected");
        Status.Classes.Set("connecting", status is "connecting" or "reconnecting");
        Status.Classes.Set("idle", status is "idle" or "degraded");
        Status.Classes.Set("stopped", status == "stopped");
        Workspace.Text = (string?)entry["workspace"] is { Length: > 0 } path ? path : Text("remote.no_default_workspace");
        if (entry["workspaces"] is JsonArray { Count: > 1 } workspaces)
        {
            AddCount("remote.n_more_projects", workspaces.Count - 1);
        }

        if ((string?)entry["kind"] == "wsl")
        {
            AddTag(Text("remote.wsl_distribution"));
        }

        if ((bool?)entry["useSSHConfig"] == true)
        {
            AddTag(Text("remote.follows_ssh_config"));
        }

        AddCount("remote.n_forwards", (int?)entry["forwards"] ?? 0);
        AddCount("remote.n_panes", (int?)entry["panes"] ?? 0);
        Probe.IsVisible = !disabled && status != "connected";
        ProbeCommand.IsVisible = status == "connected";
        ProbeCommand.Header = Text("common.test_it");
        EnableCommand.Header = Text(disabled ? "remote.turn_this_machine_on" : "remote.turn_this_machine_off");
        EditCommand.Header = Text("common.edit");
        RailCommand.Header = Text(hidden ? "remote.show_in_the_rail" : "remote.hide_from_the_rail");
        RemoveCommand.Header = Text("common.remove");
        ConfirmQuestion.Text = Text("remote.remove_name_from_the_list", ("name", name));
        AutomationProperties.SetName(More, Text("remote.more_for_name", ("name", name)));

        void AddCount(string key, int count)
        {
            if (count > 0)
            {
                AddTag(Text(key, ("n", Format.Count(count, language))));
            }
        }

        void AddTag(string value)
        {
            var tag = new Border
            {
                Height = 18,
                Padding = new Thickness(6, 0),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Child = new TextBlock
                {
                    Text = value,
                    VerticalAlignment = VerticalAlignment.Center,
                    [!TextBlock.FontSizeProperty] = StudioTypography.Size(11),
                    [!TextBlock.FontFamilyProperty] = StudioTypography.Resource("StudioMonoFamily"),
                    [!TextBlock.ForegroundProperty] = StudioTypography.Resource("GhostBrush"),
                },
            };
            tag[!Border.BorderBrushProperty] = StudioTypography.Resource("BorderBrush");
            Roots.Children.Add(tag);
        }
    }

    /// <summary>Shows or closes the row's removal confirmation.</summary>
    /// <param name="show">Whether to ask for confirmation.</param>
    internal void AskRemoval(bool show)
    {
        Confirm.IsVisible = show;
        if (show)
        {
            Remove.Focus();
        }
    }

    /// <summary>Shows the connection test under the machine it tested.</summary>
    /// <param name="text">The result text supplied by the host.</param><param name="ready">Whether the host can run.</param>
    internal void ShowProbe(string text, bool ready)
    {
        ProbeOutput.Text = text;
        ProbeResult[!Border.BorderBrushProperty] = StudioTypography.Resource(ready ? "OkBrush" : "WarnBrush");
        ProbeResult.IsVisible = true;
    }
}
