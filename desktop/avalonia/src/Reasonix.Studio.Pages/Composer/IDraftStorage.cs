namespace Reasonix.Studio.Pages.Composer;

/// <summary>
/// Where a draft's text is kept between the keystroke and the next launch.
/// </summary>
/// <remarks>
/// An interface rather than a call into a file: the rule worth testing is when a
/// draft is written, read back and dropped, not which directory it lands in. The
/// shell supplies a file-backed one.
/// </remarks>
public interface IDraftStorage
{
    /// <summary>
    /// Reads the text stored under a key.
    /// </summary>
    /// <param name="key">The draft's key.</param>
    /// <returns>The text, or empty when nothing is stored under it.</returns>
    string Read(string key);

    /// <summary>
    /// Stores the text under a key.
    /// </summary>
    /// <param name="key">The draft's key.</param>
    /// <param name="text">The text to store; empty removes the entry.</param>
    void Write(string key, string text);
}
