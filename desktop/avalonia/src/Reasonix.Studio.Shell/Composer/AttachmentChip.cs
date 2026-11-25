namespace Reasonix.Studio.Shell.Composer;

/// <summary>One reference held above the prompt.</summary>
/// <param name="Token">The host-issued token, empty for a failed drop.</param>
/// <param name="Name">The visible basename.</param>
/// <param name="SourcePath">The native path retry uses.</param>
/// <param name="Image">Whether this reference is a picture.</param>
/// <param name="Error">Why the drop was refused, empty for a ready chip.</param>
/// <param name="QuoteBody">Selected text for a quote chip, empty for an attachment.</param>
/// <param name="QuoteSource">File the quoted text came from.</param>
/// <param name="PasteBody">Long pasted text held out of the box, empty otherwise.</param>
/// <param name="KeptPath">Host file holding the paste, empty until saved.</param>
/// <param name="KeptPointer">Text that points the model at the saved file.</param>
/// <param name="Saving">Whether a save-as-file request is in flight.</param>
/// <param name="SaveError">Why saving the paste failed.</param>
internal sealed record AttachmentChip(
    string Token,
    string Name,
    string SourcePath,
    bool Image,
    string Error = "",
    string QuoteBody = "",
    string QuoteSource = "",
    string PasteBody = "",
    string KeptPath = "",
    string KeptPointer = "",
    bool Saving = false,
    string SaveError = "");
