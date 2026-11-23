namespace Reasonix.Studio.State.Transcript;

/// <summary>Disclosures with independent transcript preferences.</summary>
public enum TranscriptPart
{
    /// <summary>The model's reasoning.</summary>
    Thinking,

    /// <summary>A group of tool calls.</summary>
    Activity,

    /// <summary>An individual tool call's detail.</summary>
    Steps,

    /// <summary>The extent of long tool output.</summary>
    Output,

    /// <summary>A context compaction digest.</summary>
    Compaction,
}
