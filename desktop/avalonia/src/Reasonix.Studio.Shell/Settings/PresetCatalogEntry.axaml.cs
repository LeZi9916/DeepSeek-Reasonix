using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;
using System;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Presents one preset as a built-in card or a saved preset row.</summary>
public sealed partial class PresetCatalogEntry : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="PresetCatalogEntry"/> class.</summary>
    public PresetCatalogEntry() => InitializeComponent();

    /// <summary>Gets the action for choosing the new-task default.</summary>
    internal Button DefaultAction { get; private set; } = null!;

    /// <summary>Gets the action for viewing or editing the preset.</summary>
    internal Button ViewAction { get; private set; } = null!;

    /// <summary>Gets the action for duplicating the preset.</summary>
    internal Button CopyAction { get; private set; } = null!;

    /// <summary>Populates the appropriate catalog entry and assigns localized actions.</summary>
    /// <param name="id">The canonical preset ID.</param>
    /// <param name="name">The visible preset name.</param>
    /// <param name="description">The visible preset description.</param>
    /// <param name="selected">Whether this preset is the new-task default.</param>
    /// <param name="language">The shell language.</param>
    internal void Open(string id, string name, string description, bool selected, InterfaceLanguage language)
    {
        var builtin = id.StartsWith("builtin:", StringComparison.Ordinal);
        BuiltinCard.IsVisible = builtin;
        UserRow.IsVisible = !builtin;
        DefaultAction = builtin ? Pick : PickUser;
        ViewAction = builtin ? ViewBuiltin : ViewUser;
        CopyAction = builtin ? CopyBuiltin : CopyUser;
        foreach (var action in new[] { DefaultAction, ViewAction, CopyAction })
        {
            action.Tag = id;
        }

        var defaultLabel = MessageCatalog.Get("persona.new_task_default", language);
        var copyLabel = MessageCatalog.Get("persona.duplicate_as_custom_preset", language);
        AutomationProperties.SetName(DefaultAction, MessageCatalog.Get("persona.set_name_as_the_new", language, ("name", name)));
        AutomationProperties.SetName(ViewAction, MessageCatalog.Get(builtin ? "persona.view_name" : "persona.edit_name", language, ("name", name)));
        AutomationProperties.SetName(CopyAction, MessageCatalog.Get("persona.duplicate_name", language, ("name", name)));
        ToolTip.SetTip(CopyAction, copyLabel);
        if (builtin)
        {
            BuiltinCard.Classes.Set("selected", selected);
            BuiltinName.Text = name;
            BuiltinDescription.Text = description;
            Slug.Text = id["builtin:".Length..];
            ToolTip.SetTip(Slug, id);
            BadgeText.Text = selected ? defaultLabel : MessageCatalog.Get("common.built_in", language);
            ViewBuiltin.Content = MessageCatalog.Get("persona.mode_details", language);
            Usage.Tag = id;
            Usage.Content = MessageCatalog.Get("persona.how_to_use", language);
            UsageCopy.Text = MessageCatalog.Get(id == "builtin:general" ? "persona.everyday_partner_is_the_initial" : "persona.choose_lightweight_partner_in_a", language);
            AutomationProperties.SetName(Usage, MessageCatalog.Get("persona.how_to_use_name", language, ("name", name)));
            Usage.Click += (_, _) => UsagePanel.IsVisible = !UsagePanel.IsVisible;
        }
        else
        {
            UserName.Text = name;
            UserDescription.Text = description;
            UserBadge.IsVisible = selected;
            UserBadgeText.Text = defaultLabel;
            PickUser.IsVisible = id.StartsWith("user:", StringComparison.Ordinal);
            PickUser.IsEnabled = !selected;
            PickUser.Content = selected ? defaultLabel : MessageCatalog.Get("common.make_default", language);
            CopyUser.Content = MessageCatalog.Get("common.copy", language);
        }
    }
}
