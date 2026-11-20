using Avalonia.Controls;

namespace Reasonix.Studio.Controls;

/// <summary>
/// The right-hand panel: context, cost, jobs and the other readouts for the open
/// session.
/// </summary>
/// <remarks>
/// It declares no automation id. The captured conversation has this column
/// folded, so the baseline holds no box for it; giving the control an identity
/// the baseline never recorded would make the gate report an addition on every
/// run rather than compare anything.
/// </remarks>
public class StudioSidePanel : ContentControl
{
}
