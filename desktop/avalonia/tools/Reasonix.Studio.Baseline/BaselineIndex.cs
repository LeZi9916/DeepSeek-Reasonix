using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Reasonix.Studio.Baseline;

/// <summary>
/// The index that names every captured domain and the file holding it.
/// </summary>
public sealed class BaselineIndex
{
    /// <summary>
    /// Gets the description of the tree the values were captured from.
    /// </summary>
    [JsonPropertyName("capturedFrom")]
    public string CapturedFrom
    {
        get; init;
    } = string.Empty;

    /// <summary>
    /// Gets one entry per captured domain.
    /// </summary>
    [JsonPropertyName("domains")]
    public List<BaselineIndexEntry> Domains { get; init; } = new List<BaselineIndexEntry>();
}
