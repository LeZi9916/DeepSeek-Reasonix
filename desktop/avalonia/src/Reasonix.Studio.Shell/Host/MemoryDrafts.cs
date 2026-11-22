using Reasonix.Studio.Pages.Composer;
using System.Collections.Generic;

namespace Reasonix.Studio.Shell.Host;

/// <summary>Window-scoped drafts for previews that do not own a desktop launch.</summary>
internal sealed class MemoryDrafts : IDraftStorage
{
    /// <summary>Unsent text keyed by canonical host and conversation.</summary>
    private readonly Dictionary<string, string> _values = [];

    /// <inheritdoc/>
    public string Read(string key) => _values.GetValueOrDefault(key, string.Empty);

    /// <inheritdoc/>
    public void Write(string key, string text) => _values[key] = text;
}
