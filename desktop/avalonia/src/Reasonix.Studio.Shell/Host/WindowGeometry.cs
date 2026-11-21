namespace Reasonix.Studio.Shell.Host;

/// <summary>
/// Where and how large the window was when it last closed.
/// </summary>
/// <param name="X">The window's left edge, or null when it was never placed.</param>
/// <param name="Y">The window's top edge, or null when it was never placed.</param>
/// <param name="Width">The window's width.</param>
/// <param name="Height">The window's height.</param>
/// <param name="Maximised">Whether the window was maximised.</param>
/// <param name="RailWidth">Preferred workspace width while docked.</param>
/// <param name="Theme">Explicit light or dark theme, or null to follow the system.</param>
/// <param name="ReducedEffects">Whether to suppress decorative motion and shadows.</param>
/// <param name="QueueDuringRun">Whether to queue a follow-up when a turn is running.</param>
/// <param name="NewTaskPreset">Device-local default composition for new tasks.</param>
public sealed record WindowGeometry(double? X, double? Y, double Width, double Height, bool Maximised, double RailWidth = 264, string? Theme = null, bool ReducedEffects = false, bool QueueDuringRun = false, string? NewTaskPreset = null);
