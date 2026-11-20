using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reasonix.Studio.Contracts.Refusal;

/// <summary>
/// A refused request as the kernel writes one: a dotted code, the pieces a
/// sentence needs, and English it falls back to.
/// </summary>
/// <remarks>
/// The kernel does not speak to people. One refusal has to reach a Chinese
/// window, an English window, a log and a curl, so what crosses the boundary is
/// the identity in <see cref="Code"/>; <see cref="Error"/> is the fallback for a
/// code this build has no wording for, never the sentence a reader is shown.
///
/// It lives in the contract layer because both sides read it: the client that
/// decodes a refused answer and the page that turns it into words.
/// </remarks>
public sealed record Reason
{
    /// <summary>
    /// Gets the stable, dotted identity of the refusal.
    /// </summary>
    /// <remarks>
    /// Renaming one is a breaking change to every frontend, the same as renaming
    /// a route, so a window treats it as a value to look up rather than a phrase
    /// to parse.
    /// </remarks>
    [JsonPropertyName("code")]
    public string? Code
    {
        get; init;
    }

    /// <summary>
    /// Gets the kernel's English account of the refusal.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error
    {
        get; init;
    }

    /// <summary>
    /// Gets the values a sentence needs, such as the field that was missing.
    /// </summary>
    /// <remarks>
    /// Kept as JSON because the kernel's params are untyped by design: the
    /// wording, not the code, knows whether a value is a name or a count.
    /// </remarks>
    [JsonPropertyName("params")]
    public IReadOnlyDictionary<string, JsonElement>? Params
    {
        get; init;
    }
}
