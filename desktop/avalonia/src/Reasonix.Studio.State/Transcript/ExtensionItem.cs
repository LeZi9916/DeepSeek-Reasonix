using Reasonix.Studio.Contracts.Wire;

namespace Reasonix.Studio.State.Transcript;

/// <summary>
/// A surface an extension published into the conversation.
/// </summary>
/// <remarks>
/// A surface is addressed by plugin and surface id, so a re-publish is an update
/// in place: a status an extension refreshes while it works would otherwise pile
/// up one card per tick instead of moving where the first one landed.
/// </remarks>
public sealed record ExtensionItem : TranscriptItem
{
    /// <summary>
    /// Gets the surface the extension published.
    /// </summary>
    public required ExtensionSurface Surface
    {
        get; init;
    }
}
