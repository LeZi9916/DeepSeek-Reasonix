namespace Reasonix.Studio.Pages.Composer;

/// <summary>
/// Keeps what was typed but not yet sent, per conversation.
/// </summary>
/// <remarks>
/// Ported from the reference frontend's <c>ui/drafts.ts</c>. A draft belongs to
/// one conversation: switching sessions and coming back finds the line still
/// there, and a line sent from one session never reappears in another. Storage
/// is best-effort — a machine whose disk refuses the write still keeps the live
/// draft on screen — so neither call here throws.
/// </remarks>
public static class DraftStore
{
    /// <summary>
    /// The most text one draft may hold, in UTF-8 bytes.
    /// </summary>
    /// <remarks>
    /// Past this the draft is dropped rather than truncated: half a sentence
    /// restored into the composer reads as the person's own words and would be
    /// sent as them.
    /// </remarks>
    public const int MaxBytes = 32 * 1024;

    /// <summary>
    /// Builds the key one conversation's draft is stored under.
    /// </summary>
    /// <param name="host">Which machine's kernel the conversation runs on.</param>
    /// <param name="root">The workspace the conversation runs in.</param>
    /// <param name="path">The transcript's path, as the kernel reported it.</param>
    /// <returns>The key, or empty when there is no conversation to key it to.</returns>
    public static string KeyFor(string host, string root, string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        // One separator that cannot occur in a path, so two conversations cannot
        // build one key: a path holding the separator would otherwise let one
        // session read another's draft.
        return "draft\u001f" + host + "\u001f" + root + "\u001f" + path;
    }

    /// <summary>
    /// Reads the draft stored for one conversation.
    /// </summary>
    /// <param name="storage">Where drafts are kept.</param>
    /// <param name="key">The conversation's key.</param>
    /// <returns>The draft, or empty when it is absent, too large, or unreadable.</returns>
    public static string Read(IDraftStorage storage, string key)
    {
        if (storage is null || string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        var text = storage.Read(key) ?? string.Empty;
        return System.Text.Encoding.UTF8.GetByteCount(text) <= MaxBytes ? text : string.Empty;
    }

    /// <summary>
    /// Stores a draft, or drops it when it is empty.
    /// </summary>
    /// <param name="storage">Where drafts are kept.</param>
    /// <param name="key">The conversation's key.</param>
    /// <param name="text">The composer's line.</param>
    public static void Write(IDraftStorage storage, string key, string text)
    {
        if (storage is null || string.IsNullOrEmpty(key))
        {
            return;
        }

        var line = text ?? string.Empty;
        if (line.Length == 0 || System.Text.Encoding.UTF8.GetByteCount(line) > MaxBytes)
        {
            storage.Write(key, string.Empty);
            return;
        }

        storage.Write(key, line);
    }
}
