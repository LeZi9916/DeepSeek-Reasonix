using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>An installed package or server with its independent switch and disclosure.</summary>
internal sealed partial class ExtensionInventoryRow : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="ExtensionInventoryRow"/> class.</summary>
    public ExtensionInventoryRow()
    {
        InitializeComponent();
        Expand.Click += (_, _) => ShowDetails(!Details.IsVisible);
    }

    /// <summary>Gets the stack for the expanded inventory facts.</summary>
    internal StackPanel DetailItems => Details;

    /// <summary>Gets a value indicating whether row details are open.</summary>
    internal bool IsExpanded => Details.IsVisible;

    /// <summary>Sets identity, state, and action for the row.</summary>
    /// <param name="name">Stable inventory name.</param>
    /// <param name="summary">The contribution and source summary.</param>
    /// <param name="state">Health status displayed by the pip.</param>
    /// <param name="enabled">Effective enabled state.</param>
    /// <param name="toggleId">The switch's automation id.</param>
    /// <param name="language">The settings language.</param>
    /// <param name="write">Commits the requested state.</param>
    internal void Present(
        string name,
        string summary,
        string state,
        bool enabled,
        string toggleId,
        InterfaceLanguage language,
        Func<bool, Task<bool>> write)
    {
        Title.Text = name;
        Tag = name;
        Meta.Text = summary;
        Expand.Tag = Enabled.Tag = name;
        AutomationProperties.SetName(Expand, name);
        AutomationProperties.SetAutomationId(Enabled, toggleId);
        Enabled.IsChecked = enabled;
        SyncPip(enabled);
        Enabled.Click += async (_, _) =>
        {
            var requested = Enabled.IsChecked == true;
            Enabled.IsEnabled = false;
            try
            {
                if (await write(requested).ConfigureAwait(true))
                {
                    enabled = requested;
                }

                Enabled.IsChecked = enabled;
                SyncPip(enabled);
            }
            finally
            {
                Enabled.IsEnabled = true;
            }
        };

        void SyncPip(bool effective)
        {
            Pip.Classes.Clear();
            Pip.Classes.Add(effective ? state : "disabled");
            AutomationProperties.SetName(Enabled, MessageCatalog.Get(effective ? "common.disable_name" : "common.enable_name", language, ("name", name)));
        }
    }

    /// <summary>Switches the disclosure without changing the extension itself.</summary>
    /// <param name="open">Whether the details are shown.</param>
    internal void ShowDetails(bool open)
    {
        Details.IsVisible = open;
        Caret.RenderTransform = open ? new RotateTransform(90) : null;
    }
}
