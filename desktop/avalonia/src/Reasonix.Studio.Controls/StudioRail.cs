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
}
