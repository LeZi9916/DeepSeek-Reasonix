using Reasonix.Studio.Contracts.Wire;

namespace Reasonix.Studio.State.Transcript;

/// <summary>Device-local defaults for transcript disclosures.</summary>
/// <param name="Thinking">Reasoning disclosure mode.</param><param name="Activity">Activity group mode.</param>
/// <param name="Steps">Tool step mode.</param><param name="Output">Long output mode.</param>
/// <param name="Compaction">Compaction digest mode.</param><param name="ThinkingPreview">Whether collapsed reasoning has a one-line preview.</param>
public sealed record TranscriptFolding(
    TranscriptFoldMode Thinking = TranscriptFoldMode.Folded,
    TranscriptFoldMode Activity = TranscriptFoldMode.Live,
    TranscriptFoldMode Steps = TranscriptFoldMode.Failed,
    TranscriptFoldMode Output = TranscriptFoldMode.Folded,
    TranscriptFoldMode Compaction = TranscriptFoldMode.Folded,
    bool ThinkingPreview = false)
{
    /// <summary>Checks the same write evidence that the tool result displays.</summary>
    /// <param name="call">The canonical tool result.</param><returns>Whether it reported a file change.</returns>
    public static bool ChangedFile(Tool call) => !string.IsNullOrEmpty(call.Diff) || call.Added != 0 || call.Removed != 0;

    /// <summary>Gets the mode for a part.</summary>
    /// <param name="part">The disclosure kind.</param><returns>The local default.</returns>
    public TranscriptFoldMode Mode(TranscriptPart part) => part switch
    {
        TranscriptPart.Thinking => Thinking,
        TranscriptPart.Activity => Activity,
        TranscriptPart.Steps => Steps,
        TranscriptPart.Output => Output,
        TranscriptPart.Compaction => Compaction,
        _ => TranscriptFoldMode.Folded,
    };

    /// <summary>Replaces one mode without changing the other parts.</summary>
    /// <param name="part">The disclosure kind.</param><param name="mode">The new default.</param>
    /// <returns>The updated preferences.</returns>
    public TranscriptFolding WithMode(TranscriptPart part, TranscriptFoldMode mode) => part switch
    {
        TranscriptPart.Thinking => this with { Thinking = mode },
        TranscriptPart.Activity => this with { Activity = mode },
        TranscriptPart.Steps => this with { Steps = mode },
        TranscriptPart.Output => this with { Output = mode },
        TranscriptPart.Compaction => this with { Compaction = mode },
        _ => this,
    };

    /// <summary>Reads the default expansion from the part's facts.</summary>
    /// <param name="part">The disclosure kind.</param><param name="live">Whether it is running.</param>
    /// <param name="failed">Whether it failed.</param><param name="changed">Whether it changed a file.</param>
    /// <returns>Whether it should be expanded before the reader makes a choice.</returns>
    public bool StartsOpen(TranscriptPart part, bool live = false, bool failed = false, bool changed = false) => Mode(part) switch
    {
        TranscriptFoldMode.Live => live,
        TranscriptFoldMode.Failed => failed,
        TranscriptFoldMode.Changed => changed,
        TranscriptFoldMode.Open => true,
        _ => false,
    };
}
