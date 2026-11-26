using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Reasonix.Studio.Pages;

/// <summary>Retains a submitted or pending extension form across virtual rows.</summary>
internal sealed class ExtensionSurfaceDraft
{
    /// <summary>Raised on form submission state changes.</summary>
    internal event Action? Changed;

    /// <summary>Gets the reader's values by field identity.</summary>
    internal Dictionary<string, JsonNode?> Values { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets the acknowledged or pending state.</summary>
    internal ExtensionSubmission Submission
    {
        get;
        set;
    }

    /// <summary>Refreshes the mounted view after a submission finishes.</summary>
    internal void Notify() => Changed?.Invoke();
}
