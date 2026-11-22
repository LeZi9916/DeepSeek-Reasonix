using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.I18n;
using System;

namespace Reasonix.Studio.Pages.Rail;

/// <summary>A project disclosure with its canonical path as the accessible identity.</summary>
internal sealed partial class ProjectEntry : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="ProjectEntry"/> class.</summary>
    /// <param name="workspace">Canonical project.</param>
    /// <param name="folded">Whether its sessions are hidden.</param>
    /// <param name="toggle">Changes the disclosure.</param>
    /// <param name="host">Remote machine, or null for local.</param><param name="language">Interface language.</param>
    internal ProjectEntry(TreeWorkspace workspace, bool folded, Action toggle, RemoteRailHost? host = null, InterfaceLanguage language = InterfaceLanguage.Zh)
    {
        InitializeComponent();
        Title.Text = workspace.Name;
        Opacity = workspace.Missing ? 0.5 : 1;
        Chevron.Data = Geometry.Parse(folded ? "m9 6 6 6-6 6" : "m6 9 6 6 6-6");
        Disclosure.Opacity = workspace.Sessions.Count > 0 || workspace.Total > 0 ? 1 : 0;
        Host.Text = host?.Name;
        State.Text = host?.Status switch
        {
            "connecting" or "reconnecting" => MessageCatalog.Get("remote.connecting_short", language),
            "stopped" => MessageCatalog.Get("remote.offline", language),
            "idle" => MessageCatalog.Get("common.not_connected", language),
            _ => string.Empty,
        };
        if (host?.Status == "stopped")
        {
            State.Bind(TextBlock.ForegroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("ErrInkBrush"));
        }

        var step = host?.Step switch
        {
            "detect" => "app.detecting_the_platform",
            "install" => "app.installing_reasonix",
            "waiting_lock" => "app.waiting_for_another_client",
            "launch" => "app.starting_serve",
            "health_check" => "app.waiting_for_it_to_become",
            "ready" => "app.attaching_the_port_forward",
            "reuse" => "app.reusing_the_running_instance",
            _ => host?.Step,
        };
        Detail.Text = host?.Status is "connecting" or "reconnecting" ? MessageCatalog.Get(step ?? string.Empty, language) : host?.Error;
        Connection.IsVisible = !string.IsNullOrEmpty(Detail.Text);
        AutomationProperties.SetName(Fold, workspace.Name);
        ToolTip.SetTip(Fold, workspace.Root);
        ToolTip.SetTip(Host, host?.Target);
        Fold.Click += (_, _) => toggle();
    }
}
