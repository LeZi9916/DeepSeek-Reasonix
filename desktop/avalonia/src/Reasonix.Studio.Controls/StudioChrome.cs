using Avalonia.Automation;
using Avalonia.Controls;

namespace Reasonix.Studio.Controls;

/// <summary>
/// The bar across the top of the page area: the workspace crumb and the window's
/// tools.
/// </summary>
/// <remarks>
/// It sits beside the rail rather than above it, matching the baseline where
/// <c>.chrome</c> starts at the rail's right edge.
/// </remarks>
public class StudioChrome : ContentControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StudioChrome"/> class.
    /// </summary>
    /// <remarks>
    /// The automation id is the identity the captured baseline recorded for this
    /// region (<c>class:chrome</c>).
    /// </remarks>
    public StudioChrome()
    {
        AutomationProperties.SetAutomationId(this, "class:chrome");
    }
}
