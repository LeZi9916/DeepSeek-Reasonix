using Avalonia;
using Avalonia.Controls;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>A counted installed extension group in the settings page.</summary>
internal sealed partial class ExtensionSection : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="ExtensionSection"/> class.</summary>
    public ExtensionSection() => InitializeComponent();

    /// <summary>Places the group copy and the inventory within its reference card.</summary>
    /// <param name="heading">Localized section name.</param>
    /// <param name="hint">Localized description.</param>
    /// <param name="scope">Optional workspace-scope note.</param>
    /// <param name="content">The section's interactive inventory.</param>
    internal void Open(string heading, string hint, string? scope, Control content)
    {
        Heading.Text = heading;
        Hint.Text = hint;
        ScopeBadge.IsVisible = scope is not null;
        ScopeNote.Text = scope;
        Items.Content = content;
    }

    /// <summary>Uses the compact settings card spacing in a narrow viewport.</summary>
    /// <param name="compact">Whether the settings viewport is narrow.</param>
    internal void SetCompact(bool compact) => Card.Padding = compact ? new Thickness(15, 17, 15, 15) : new Thickness(22, 20, 22, 18);

    /// <summary>Displays the inventory count in the right-hand pill.</summary>
    /// <param name="count">The localized count, or null for an empty group.</param>
    internal void Count(string? count)
    {
        CountLabel.Text = count;
        CountBadge.IsVisible = count is not null;
    }
}
