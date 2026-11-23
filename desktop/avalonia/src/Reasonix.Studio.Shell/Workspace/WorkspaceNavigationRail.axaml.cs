using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Host;
using Reasonix.Studio.Shell.Settings;
using System;

namespace Reasonix.Studio.Shell.Workspace;

/// <summary>The optional column of workspace destination buttons.</summary>
public sealed partial class WorkspaceNavigationRail : SettingsFormView
{
    /// <summary>Initializes a new instance of the <see cref="WorkspaceNavigationRail"/> class.</summary>
    public WorkspaceNavigationRail()
    {
        InitializeComponent();
        New.Click += (_, _) => Requested?.Invoke("new");
        Home.Click += (_, _) => Requested?.Invoke("home");
        Account.Click += (_, _) => Requested?.Invoke("account");
        Settings.Click += (_, _) => Requested?.Invoke("settings");
    }

    /// <summary>Raised for the selected destination.</summary>
    internal event Action<string>? Requested;

    /// <summary>Renders the current ordered destinations.</summary>
    /// <param name="preferences">The device layout.</param>
    /// <param name="language">The interface language.</param>
    internal void Draw(WorkspacePreferences preferences, InterfaceLanguage language)
    {
        Translate(language);
        Label(New, Text("common.start_a_new_session"));
        Label(Home, Text("common.session"));
        Label(Account, Text("settings.account"));
        Label(Settings, Text("common.settings"));
        Entries.Children.Clear();
        foreach (var item in WorkspaceDestinations.Order(preferences))
        {
            var button = new Button
            {
                Tag = item.Id,
                Content = Glyph(item.Glyph),
                Classes = { "preset-flat", "workspace-nav-icon" },
            };
            Label(button, Text(item.Label));
            AutomationProperties.SetAutomationId(button, "action:navigation.rail");
            button.Click += (_, _) => Requested?.Invoke(item.Id);
            Entries.Children.Add(button);
        }

        static void Label(Button button, string label)
        {
            AutomationProperties.SetName(button, label);
            ToolTip.SetTip(button, label);
        }
    }

    /// <summary>Draws the shared icon in a navigation button.</summary>
    /// <param name="key">Geometry resource ID.</param>
    /// <returns>The scalable icon.</returns>
    internal Control Glyph(string key) => new Viewbox
    {
        Width = 16,
        Height = 16,
        Child = new Canvas
        {
            Width = 24,
            Height = 24,
            Children =
            {
                new Avalonia.Controls.Shapes.Path
                {
                    Data = (Geometry)this.FindResource(key)!,
                    StrokeThickness = 1.8,
                    StrokeLineCap = PenLineCap.Round,
                    StrokeJoin = PenLineJoin.Round,
                    [!Shape.StrokeProperty] = StudioTypography.Resource("MutedBrush"),
                },
            },
        },
    };
}
