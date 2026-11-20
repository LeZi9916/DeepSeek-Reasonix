namespace Reasonix.Studio.Controls;

/// <summary>
/// The load-bearing sizes of the shell skeleton, taken from the captured baseline.
/// </summary>
/// <remarks>
/// These are constants rather than literals in each template so a test can hold
/// them to the baseline the geometry gate reads. A rail that is 260 wide instead
/// of 264 would otherwise pass review and fail the gate only after the whole
/// page around it had been built to the wrong column.
/// </remarks>
public static class SkeletonMetrics
{
    /// <summary>
    /// Gets the rail's width, matching the baseline's <c>.rail</c> box.
    /// </summary>
    public const double RailWidth = 264;

    /// <summary>
    /// Gets the chrome bar's height, matching the baseline's <c>.chrome</c> box.
    /// </summary>
    public const double ChromeHeight = 48;

    /// <summary>
    /// Gets the composer's width, matching the baseline's <c>.compose</c> box.
    /// </summary>
    public const double ComposerWidth = 760;

    /// <summary>
    /// Gets the composer's height, matching the baseline's <c>.compose</c> box.
    /// </summary>
    public const double ComposerHeight = 128;

    /// <summary>
    /// Gets the composer's top-left corner radius, matching the baseline.
    /// </summary>
    public const double ComposerRadius = 15;
}
