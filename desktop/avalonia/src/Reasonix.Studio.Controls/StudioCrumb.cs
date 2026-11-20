using Avalonia.Automation;
using Avalonia.Controls;

namespace Reasonix.Studio.Controls;

/// <summary>
/// The conversation's identity in the chrome: the folder, the project and the
/// session name.
/// </summary>
public class StudioCrumb : ContentControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StudioCrumb"/> class.
    /// </summary>
    /// <remarks>
    /// The automation id is the identity the captured baseline recorded for the
    /// crumb (<c>class:crumb</c>).
    /// </remarks>
    public StudioCrumb()
    {
        AutomationProperties.SetAutomationId(this, "class:crumb");
    }
}
