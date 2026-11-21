namespace Reasonix.Studio.Kernel.Client;

/// <summary>A completion menu and the UTF-16 span it replaces.</summary>
/// <param name="From">Inclusive replacement start.</param>
/// <param name="To">Exclusive replacement end.</param>
/// <param name="Items">Available completions.</param>
/// <param name="Typed">Already fully typed command, when present.</param>
/// <param name="Kind">The kind of token being completed.</param>
public sealed record ComposerCompletion(
    int From,
    int To,
    CompletionChoice[] Items,
    CompletionChoice? Typed = null,
    string? Kind = null);
