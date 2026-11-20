namespace Reasonix.Studio.Kernel.Client;

/// <summary>
/// A request to start a turn, in the shape the kernel's inbox takes.
/// </summary>
/// <param name="Input">The line as the person typed it.</param>
/// <param name="Submit">What the model reads, with any chips resolved.</param>
public sealed record SubmitRequest(string Input, string Submit);
