using Reasonix.Studio.State.Transcript;
using System.Collections.Generic;

namespace Reasonix.Studio.Pages;

/// <summary>The reader's explicit choices for one retained transcript pane.</summary>
public sealed class TranscriptDisclosureChoices
{
    /// <summary>Gets the manual choices scoped by row identity and disclosure kind.</summary>
    internal Dictionary<(TranscriptPart Part, string Id), bool> Overrides { get; } = new();

    /// <summary>Gets or sets a value indicating whether intermediate work is visible in this pane.</summary>
    internal bool WorkOpen
    {
        get; set;
    }
}
