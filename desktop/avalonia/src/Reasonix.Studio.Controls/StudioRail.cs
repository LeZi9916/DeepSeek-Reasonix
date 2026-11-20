using Avalonia.Automation;
using Avalonia.Controls;

namespace Reasonix.Studio.Controls;

/// <summary>
/// The full-height navigation rail: the project's brand, its search field and the
/// workspace tree.
/// </summary>
/// <remarks>
/// A content control so a page composes whatever the tree needs inside it; its
/// width comes from one place — <see cref="SkeletonMetrics.RailWidth"/> — rather
/// than from whichever page happens to host it.
/// </remarks>
public class StudioRail : ContentControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StudioRail"/> class.
    /// </summary>
    /// <remarks>
    /// The automation id is the identity the captured baseline recorded for this
    /// region (<c>class:rail</c>), so the geometry gate compares this control
    /// against the rail the reference frontend drew rather than against a
    /// selector someone typed twice.
    /// </remarks>
    public StudioRail()
    {
        AutomationProperties.SetAutomationId(this, "class:rail");
    }
}
