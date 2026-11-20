using Avalonia.Automation;
using Avalonia.Controls;

namespace Reasonix.Studio.Controls;

/// <summary>
/// The line a person writes into: text, the model choice and the send control.
/// </summary>
public class StudioComposer : ContentControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StudioComposer"/> class.
    /// </summary>
    /// <remarks>
    /// The automation id is the identity the captured baseline recorded for this
    /// region (<c>class:compose</c>).
    /// </remarks>
    public StudioComposer()
    {
        AutomationProperties.SetAutomationId(this, "class:compose");
    }
}
