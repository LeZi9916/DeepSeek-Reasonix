namespace Reasonix.Studio.Kernel.Client;

/// <summary>A single kernel-authored composer completion.</summary>
/// <param name="Label">Visible item name.</param>
/// <param name="Insert">Verbatim insertion.</param>
/// <param name="Hint">Optional description.</param>
/// <param name="Descend">Whether choosing the item opens its children.</param>
/// <param name="Kind">Item kind from the host.</param>
public sealed record CompletionChoice(
    string Label,
    string Insert,
    string? Hint = null,
    bool Descend = false,
    string? Kind = null);
