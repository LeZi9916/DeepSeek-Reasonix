using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;
using System.Collections.Generic;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Discloses the kernel's list of settings that differ from its defaults.</summary>
public sealed partial class ChangedSettings : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="ChangedSettings"/> class.</summary>
    public ChangedSettings() => InitializeComponent();

    /// <summary>Displays the captured setting keys in kernel order.</summary>
    /// <param name="keys">Changed keys.</param>
    /// <param name="language">Settings language.</param>
    internal void Show(IReadOnlyList<string> keys, InterfaceLanguage language)
    {
        Summary.Text = keys.Count == 0
            ? MessageCatalog.Get("settings.every_setting_is_the_default", language)
            : MessageCatalog.Get("settings.n_differ_from_the_defaults", language, ("n", Format.Count(keys.Count, language)));
        Keys.ItemsSource = keys;
        ToggleKeys.IsVisible = keys.Count > 0;
        ToggleKeys.Content = MessageCatalog.Get("settings.look_at", language);
        ToggleKeys.Click += (_, _) =>
        {
            Details.IsVisible = !Details.IsVisible;
            ToggleKeys.Content = MessageCatalog.Get(Details.IsVisible ? "common.collapse" : "settings.look_at", language);
        };
    }
}
