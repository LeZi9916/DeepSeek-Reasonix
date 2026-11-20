using System.Globalization;

namespace Reasonix.Studio.Pages.I18n;

/// <summary>
/// Turns the language a person configured — or the one their machine reports —
/// into the language the interface draws in.
/// </summary>
/// <remarks>
/// Ported from the reference frontend's <c>i18n/index.ts</c>. The rule that
/// matters is that only an explicit <c>zh</c> or <c>en</c> is taken as a choice;
/// anything else, including <c>auto</c> and an empty preference, follows the
/// machine. A machine set to French with Chinese second is a French machine, and
/// the answer for it is English.
/// </remarks>
public static class LanguageResolver
{
    /// <summary>
    /// The preference that means "use what the machine is set to".
    /// </summary>
    public const string Auto = "auto";

    /// <summary>
    /// Resolves a stored or configured preference into a language.
    /// </summary>
    /// <param name="preference">The preference: <c>zh</c>, <c>en</c>, <c>auto</c>, empty, or null.</param>
    /// <param name="machineTags">
    /// The machine's language tags, most preferred first. Only the first is read.
    /// </param>
    /// <returns>The language to draw in.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="machineTags"/> is null.</exception>
    public static InterfaceLanguage Resolve(string? preference, IReadOnlyList<string> machineTags)
    {
        ArgumentNullException.ThrowIfNull(machineTags);

        var wanted = (preference ?? string.Empty).Trim().ToLowerInvariant();
        if (wanted == "zh")
        {
            return InterfaceLanguage.Zh;
        }

        if (wanted == "en")
        {
            return InterfaceLanguage.En;
        }

        return FromMachine(machineTags);
    }

    /// <summary>
    /// Reads the machine's own preference.
    /// </summary>
    /// <param name="machineTags">The machine's language tags, most preferred first.</param>
    /// <returns>Chinese for a Chinese machine, otherwise English.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="machineTags"/> is null.</exception>
    /// <remarks>
    /// English is the fallback rather than a claim that the machine is English:
    /// this interface has two languages, and a machine set to neither is read in
    /// the one that is not Chinese.
    /// </remarks>
    public static InterfaceLanguage FromMachine(IReadOnlyList<string> machineTags)
    {
        ArgumentNullException.ThrowIfNull(machineTags);
        if (machineTags.Count == 0)
        {
            return InterfaceLanguage.En;
        }

        return IsChinese(machineTags[0]) ? InterfaceLanguage.Zh : InterfaceLanguage.En;
    }

    /// <summary>
    /// Reads the machine's preference from the running process.
    /// </summary>
    /// <returns>Chinese for a Chinese machine, otherwise English.</returns>
    public static InterfaceLanguage FromMachine()
    {
        return IsChinese(CultureInfo.CurrentUICulture.Name) ? InterfaceLanguage.Zh : InterfaceLanguage.En;
    }

    /// <summary>
    /// Reads whether a language tag names Chinese.
    /// </summary>
    /// <param name="tag">A BCP-47 tag, or a tag a system reported in its place.</param>
    /// <returns>True for every kind of Chinese this interface draws in.</returns>
    /// <remarks>
    /// Matched on the tag rather than on a list of regions, so a locale nobody
    /// thought of (<c>zh-Hans-SG</c>, <c>cmn-Hans-CN</c>) still lands where it
    /// belongs. Simplified, traditional, Hong Kong, Singapore, Cantonese and
    /// Mandarin are one Chinese here, because the interface has one Chinese.
    /// </remarks>
    public static bool IsChinese(string? tag)
    {
        var text = (tag ?? string.Empty).Trim().ToLowerInvariant();
        return text.StartsWith("zh", StringComparison.Ordinal)
            || text.StartsWith("yue", StringComparison.Ordinal)
            || text.StartsWith("cmn", StringComparison.Ordinal)
            || text.Contains("hans", StringComparison.Ordinal)
            || text.Contains("hant", StringComparison.Ordinal);
    }
}
