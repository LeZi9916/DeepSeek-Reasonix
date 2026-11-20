namespace Reasonix.Studio.Shell.Host;

/// <summary>
/// Where and how large the window was when it last closed.
/// </summary>
/// <param name="X">The window's left edge, or null when it was never placed.</param>
/// <param name="Y">The window's top edge, or null when it was never placed.</param>
/// <param name="Width">The window's width.</param>
/// <param name="Height">The window's height.</param>
/// <param name="Maximised">Whether the window was maximised.</param>
public sealed record WindowGeometry(double? X, double? Y, double Width, double Height, bool Maximised);
