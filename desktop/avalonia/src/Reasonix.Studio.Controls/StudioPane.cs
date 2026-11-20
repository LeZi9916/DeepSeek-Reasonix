using Avalonia.Automation;
using Avalonia.Controls;

namespace Reasonix.Studio.Controls;

/// <summary>
/// One conversation's surface: the transcript, the side panel and the composer.
/// </summary>
public class StudioPane : ContentControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StudioPane"/> class.
    /// </summary>
    /// <remarks>
    /// The automation id is the identity the captured baseline recorded for this
    /// region (<c>class:pane</c>).
    /// </remarks>
    public StudioPane()
    {
        AutomationProperties.SetAutomationId(this, "class:pane");
    }
}
