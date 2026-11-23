using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>A titled settings group within a card.</summary>
internal sealed partial class SettingsGroup : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="SettingsGroup"/> class.</summary>
    /// <param name="key">The title and navigation key.</param><param name="hint">The descriptive message key.</param>
    /// <param name="body">The group contents.</param><param name="language">The window language.</param>
    /// <param name="scope">The scope key when different from the page default.</param>
    internal SettingsGroup(string key, string? hint, Control body, InterfaceLanguage language, string? scope = null)
    {
        InitializeComponent();
        Tag = key;
        Heading.Text = MessageCatalog.Get(key, language);
        Hint.IsVisible = hint is not null;
        Hint.Text = hint is null ? null : MessageCatalog.Get(hint, language);
        if (hint is null)
        {
            Items.Margin = default;
        }

        ScopeBadge.IsVisible = scope is not null;
        ScopeLabel.Text = scope is null ? null : MessageCatalog.Get(scope, language);
        Items.Content = body;
    }
}
