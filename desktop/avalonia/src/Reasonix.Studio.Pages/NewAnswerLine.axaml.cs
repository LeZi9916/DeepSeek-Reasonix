using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;

namespace Reasonix.Studio.Pages;

/// <summary>The transcript separator at the start of an unread span.</summary>
internal sealed partial class NewAnswerLine : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="NewAnswerLine"/> class.</summary>
    /// <param name="language">The reader's language.</param>
    internal NewAnswerLine(InterfaceLanguage language)
    {
        InitializeComponent();
        Label.Text = MessageCatalog.Get("chat.new_answers_start_here", language);
        AutomationProperties.SetName(this, Label.Text);
    }

    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer() => new SeparatorPeer(this);

    /// <summary>Exposes the divider as a separator instead of an actionable control.</summary>
    /// <param name="owner">The visible divider.</param>
    private sealed class SeparatorPeer(NewAnswerLine owner) : ControlAutomationPeer(owner)
    {
        /// <inheritdoc/>
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Separator;
    }
}
