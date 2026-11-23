namespace Reasonix.Studio.State.Transcript;

/// <summary>The default expansion of a transcript disclosure.</summary>
public enum TranscriptFoldMode
{
    /// <summary>Starts collapsed.</summary>
    Folded,

    /// <summary>Expands while the relevant work is running.</summary>
    Live,

    /// <summary>Expands failed steps outside an activity group.</summary>
    Failed,

    /// <summary>Expands when the tool reported a file change.</summary>
    Changed,

    /// <summary>Starts expanded.</summary>
    Open,
}
