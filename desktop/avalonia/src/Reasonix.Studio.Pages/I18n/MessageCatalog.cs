using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Reasonix.Studio.Pages.I18n;

/// <summary>
/// The one place the interface turns a message key into the words a reader sees.
/// </summary>
/// <remarks>
/// A missing key returns the key itself and records it, rather than throwing. A
/// window that lost a phrase still draws: the reader sees a name they can report,
/// and the session stays usable. Throwing here would turn a wording gap into a
/// blank page, which is the worse failure for an interface whose job is to show
/// what the kernel said.
/// </remarks>
public static class MessageCatalog
{
    /// <summary>
    /// How many distinct keys the diagnostics will remember.
    /// </summary>
    private const int MissingLimit = 256;

    /// <summary>
    /// Matches a placeholder, which the tables write as <c>{name}</c>.
    /// </summary>
    private static readonly Regex s_placeholder =
        new Regex("\\{(?<name>\\w+)\\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The keys asked for and not found, in the order they were first missed.
    /// </summary>
    /// <remarks>
    /// Bounded: a page that renders an unknown key per row would otherwise grow
    /// this for as long as the window is open. Once the limit is reached the set
    /// stops growing and a count keeps rising, so a report still says how bad it
    /// is without the list being the memory.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, byte> s_missing = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

    /// <summary>
    /// How many misses were seen after the list filled up.
    /// </summary>
    private static long s_suppressed;

    /// <summary>
    /// Gets how many misses were counted but not recorded because the list was full.
    /// </summary>
    public static long SuppressedMisses => Interlocked.Read(ref s_suppressed);

    /// <summary>
    /// Gets the keys that were asked for and not found.
    /// </summary>
    /// <returns>A snapshot of the recorded keys, ordered, for a log or a report.</returns>
    public static IReadOnlyList<string> MissingKeys()
    {
        return s_missing.Keys.OrderBy(key => key, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Forgets every recorded miss. A test uses it to start from a known state.
    /// </summary>
    public static void ResetDiagnostics()
    {
        s_missing.Clear();
        Interlocked.Exchange(ref s_suppressed, 0);
    }

    /// <summary>
    /// Reads one message, with its placeholders filled.
    /// </summary>
    /// <param name="key">The key the interface names the message by.</param>
    /// <param name="language">The language to read it in.</param>
    /// <param name="variables">Values for the placeholders, or null when it has none.</param>
    /// <returns>
    /// The message, or <paramref name="key"/> when the table does not hold it.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is null.</exception>
    /// <remarks>
    /// A placeholder with no value stays as written, so a caller that forgot one
    /// produces a visibly incomplete sentence rather than a sentence that reads
    /// as complete and says the wrong thing.
    /// </remarks>
    public static string Get(string key, InterfaceLanguage language, IReadOnlyDictionary<string, string>? variables = null)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!MessageTables.All.TryGetValue(key, out var message))
        {
            Record(key);
            return key;
        }

        var text = language == InterfaceLanguage.Zh ? message.Zh : message.En;
        return variables is null ? text : Fill(text, variables);
    }

    /// <summary>
    /// Reads one message and fills its placeholders from named values.
    /// </summary>
    /// <param name="key">The key the interface names the message by.</param>
    /// <param name="language">The language to read it in.</param>
    /// <param name="variables">Values for the placeholders, as name and text.</param>
    /// <returns>The message with its placeholders filled, or the key when it is unknown.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is null.</exception>
    public static string Get(string key, InterfaceLanguage language, params (string Name, string Value)[] variables)
    {
        ArgumentNullException.ThrowIfNull(variables);

        var map = new Dictionary<string, string>(variables.Length, StringComparer.Ordinal);
        foreach (var (name, value) in variables)
        {
            map[name] = value;
        }

        return Get(key, language, map);
    }

    /// <summary>
    /// Checks whether the table holds a key, without recording a miss.
    /// </summary>
    /// <param name="key">The key to look for.</param>
    /// <returns>True when a call to <see cref="Get(string, InterfaceLanguage, IReadOnlyDictionary{string, string}?)"/> would find words.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is null.</exception>
    public static bool Has(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return MessageTables.All.ContainsKey(key);
    }

    /// <summary>
    /// Fills the placeholders a message holds.
    /// </summary>
    /// <param name="text">The message as the table spells it.</param>
    /// <param name="variables">The values, by placeholder name.</param>
    /// <returns>The message with every known placeholder replaced.</returns>
    private static string Fill(string text, IReadOnlyDictionary<string, string> variables)
    {
        return s_placeholder.Replace(text, match =>
        {
            var name = match.Groups["name"].Value;
            return variables.TryGetValue(name, out var value) ? value : match.Value;
        });
    }

    /// <summary>
    /// Records a miss, within the bound this keeps.
    /// </summary>
    /// <param name="key">The key that was not found.</param>
    private static void Record(string key)
    {
        if (s_missing.Count >= MissingLimit)
        {
            Interlocked.Increment(ref s_suppressed);
            return;
        }

        s_missing.TryAdd(key, 0);
    }
}
