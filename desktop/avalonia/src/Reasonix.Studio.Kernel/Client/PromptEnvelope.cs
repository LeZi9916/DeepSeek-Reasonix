using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>
/// The text one queued entry carries, as the kernel froze it when it was queued.
/// </summary>
/// <remarks>
/// <see cref="DisplayText"/> is what the person typed; <see cref="SubmitText"/> is
/// what the model reads once chips are resolved. A line taken back into the
/// composer goes back as the display text, because that is what it was.
/// </remarks>
public sealed record PromptEnvelope
{
    /// <summary>
    /// Gets the line as the person wrote it.
    /// </summary>
    [JsonPropertyName("displayText")]
    public string DisplayText { get; init; } = string.Empty;

    /// <summary>
    /// Gets the line as the person wrote it, before any rewriting.
    /// </summary>
    [JsonPropertyName("rawText")]
    public string RawText { get; init; } = string.Empty;

    /// <summary>
    /// Gets what the model will read.
    /// </summary>
    [JsonPropertyName("submitText")]
    public string SubmitText { get; init; } = string.Empty;

    /// <summary>
    /// Gets the line a reader should see for this entry.
    /// </summary>
    /// <remarks>
    /// The display text when it was recorded, then the raw text, then the submit
    /// text: an older entry may carry only the last of them, and an empty line is
    /// the one answer that helps nobody.
    /// </remarks>
    public string Readable
    {
        get
        {
            if (!string.IsNullOrEmpty(DisplayText))
            {
                return DisplayText;
            }

            return !string.IsNullOrEmpty(RawText) ? RawText : SubmitText;
        }
    }
}
