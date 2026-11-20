using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>
/// One conversation the session directory holds.
/// </summary>
public sealed record SessionEntry
{
    /// <summary>
    /// Gets the file name the conversation is stored under.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the transcript's path, which is what a resume names.
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Gets the title the conversation was given.
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Gets how many turns the conversation holds.
    /// </summary>
    [JsonPropertyName("turns")]
    public int Turns
    {
        get; init;
    }

    /// <summary>
    /// Gets a value indicating whether this is the conversation already open.
    /// </summary>
    [JsonPropertyName("current")]
    public bool Current
    {
        get; init;
    }

    /// <summary>
    /// Gets a value indicating whether the conversation has anything unread.
    /// </summary>
    [JsonPropertyName("unread")]
    public bool Unread
    {
        get; init;
    }

    /// <summary>
    /// Gets the title to draw: the conversation's own, or its file name when it has
    /// none.
    /// </summary>
    /// <returns>What a picker shows for this row.</returns>
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? Name : Title;
}
