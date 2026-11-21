namespace Reasonix.Studio.Kernel.Client;

/// <summary>A file reference or individual refusal from the drop endpoint.</summary>
/// <param name="Ref">The token the model-side parser accepts.</param>
/// <param name="Path">The path named by the host.</param>
/// <param name="Error">Why the path could not be used.</param>
public sealed record DroppedReference(string? Ref, string? Path, string? Error);
