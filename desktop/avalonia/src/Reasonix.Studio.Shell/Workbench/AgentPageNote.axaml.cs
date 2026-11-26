using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System.Globalization;
using System.Text;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>Displays reported browser metadata as inert, selectable text.</summary>
internal sealed partial class AgentPageNote : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="AgentPageNote"/> class.</summary>
    internal AgentPageNote() => InitializeComponent();

    /// <summary>Gets the identifier of the currently described page.</summary>
    internal string Target
    {
        get; private set;
    } = string.Empty;

    /// <summary>Reads the label for a browser tab.</summary>
    /// <param name="tab">Canonical page metadata.</param><param name="language">Interface language.</param><returns>The tab label.</returns>
    internal static string Label(AgentBrowserTab tab, InterfaceLanguage language)
    {
        var title = Plain(tab.Title);
        if (title.Length > 0)
        {
            return title;
        }

        var url = Plain(tab.Url);
        return url.Length > 0 ? url : MessageCatalog.Get("common.blank_page", language);
    }

    /// <summary>Updates the title, address and activity using the latest session read.</summary>
    /// <param name="tab">Canonical page metadata.</param><param name="language">Interface language.</param>
    internal void Show(AgentBrowserTab tab, InterfaceLanguage language)
    {
        Target = tab.Target;
        var title = Plain(tab.Title);
        var url = Plain(tab.Url);
        PageTitle.Text = title.Length > 0 ? title : MessageCatalog.Get("common.blank_page", language);
        PageAddress.Text = url;
        PageAddress.IsVisible = url.Length > 0;
        PageStatus.Text = MessageCatalog.Get("workbench.the_model_is_working_on", language);
        PageStatus.IsVisible = tab.Active;
        ReadOnlyHint.Text = MessageCatalog.Get("persona.read_only_the_page_itself", language);
        AutomationProperties.SetName(this, MessageCatalog.Get("persona.page_the_agent_is_viewing", language));
    }

    /// <summary>Bounds metadata labels and removes invisible controls before displaying them.</summary>
    /// <param name="value">The reported title or address.</param><returns>Plain display text.</returns>
    private static string Plain(string? value)
    {
        var text = new StringBuilder();
        var space = false;
        foreach (var rune in (value ?? string.Empty).EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                space = text.Length > 0;
                continue;
            }

            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Format or UnicodeCategory.Control)
            {
                continue;
            }

            if (text.Length >= 500)
            {
                text.Append('…');
                break;
            }

            if (space)
            {
                text.Append(' ');
                space = false;
            }

            text.Append(rune.ToString());
        }

        return text.ToString();
    }
}
